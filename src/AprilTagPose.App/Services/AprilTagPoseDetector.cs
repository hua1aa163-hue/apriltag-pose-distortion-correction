using System.Diagnostics;
using AprilTagPose.Models;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using CvPoint = OpenCvSharp.Point;

namespace AprilTagPose.Services;

/// <summary>使用指定 OpenCV 方形标记字典进行检测与单标签位姿估计。</summary>
public sealed class AprilTagPoseDetector : IDisposable
{
    public const MarkerDictionaryKind DefaultDictionary = MarkerDictionaryKind.ArUco4X4_250;
    public const double MaximumAcceptedReprojectionErrorPixels = 5d;

    private readonly OpenCvSharp.Aruco.Dictionary _dictionary;
    private readonly DetectorParameters _parameters;
    private readonly RefineParameters _refineParameters;
    private readonly ArucoDetector _detector;
    private readonly object _sync = new();
    private bool _disposed;

    public AprilTagPoseDetector(MarkerDictionaryKind dictionaryKind = DefaultDictionary)
    {
        DictionaryKind = dictionaryKind;
        PredefinedDictionaryType dictionaryType = dictionaryKind switch
        {
            MarkerDictionaryKind.ArUco4X4_250 => PredefinedDictionaryType.Dict4X4_250,
            MarkerDictionaryKind.AprilTag16h5 => PredefinedDictionaryType.DictAprilTag_16h5,
            _ => throw new ArgumentOutOfRangeException(nameof(dictionaryKind), dictionaryKind, "不支持的标记字典。")
        };
        _dictionary = CvAruco.GetPredefinedDictionary(dictionaryType);
        _parameters = new DetectorParameters
        {
            CornerRefinementMethod = CornerRefineMethod.Subpix,
            CornerRefinementWinSize = 5,
            CornerRefinementMaxIterations = 50,
            CornerRefinementMinAccuracy = 0.01,
            MinMarkerPerimeterRate = 0.015,
            ErrorCorrectionRate = dictionaryKind == MarkerDictionaryKind.ArUco4X4_250 ? 1.0 : 0.6
        };
        _refineParameters = new RefineParameters();
        _detector = new ArucoDetector(_dictionary, _parameters, _refineParameters);
    }

    public MarkerDictionaryKind DictionaryKind { get; }

    public DetectionFrameResult Detect(
        Mat source,
        CameraCalibration? configuredCalibration,
        double tagSizeMillimeters,
        DateTimeOffset? capturedAt = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        if (source.Empty()) throw new ArgumentException("待检测图像为空。", nameof(source));
        if (!double.IsFinite(tagSizeMillimeters) || tagSizeMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(tagSizeMillimeters), "标签边长必须大于 0 mm。");

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stopwatch stopwatch = Stopwatch.StartNew();
            using Mat gray = ToGray8(source);
            Mat annotated = ToBgr8(source);
            bool approximate = configuredCalibration?.IsValid != true;
            CameraCalibration calibration = approximate
                ? CameraCalibration.CreateApproximate(source.Width, source.Height)
                : configuredCalibration!.ForImageSize(source.Width, source.Height);

            try
            {
                _detector.DetectMarkers(gray, out Point2f[][] corners, out int[] ids, out Point2f[][] rejected);
                if (ids.Length > 0)
                {
                    CvAruco.DrawDetectedMarkers(annotated, corners, ids, new Scalar(0, 220, 255));
                }
                else if (rejected.Length > 0)
                {
                    DrawRejectedCandidates(annotated, rejected, DictionaryKind.ShortName());
                }

                var tags = new List<TagPoseResult>(ids.Length);
                for (int index = 0; index < ids.Length; index++)
                {
                    TagPoseResult result = EstimatePose(
                        ids[index], corners[index], annotated, calibration,
                        tagSizeMillimeters, approximate);
                    tags.Add(result);
                }

                tags.Sort(static (a, b) => a.Id.CompareTo(b.Id));
                stopwatch.Stop();
                return new DetectionFrameResult
                {
                    AnnotatedImage = annotated,
                    Dictionary = DictionaryKind,
                    Tags = tags,
                    CalibrationUsed = calibration,
                    UsesApproximateIntrinsics = approximate,
                    ProcessingTime = stopwatch.Elapsed,
                    RejectedCandidateCount = rejected.Length,
                    CapturedAt = capturedAt ?? DateTimeOffset.Now
                };
            }
            catch
            {
                annotated.Dispose();
                throw;
            }
        }
    }

    private static TagPoseResult EstimatePose(
        int id,
        Point2f[] corners,
        Mat annotated,
        CameraCalibration calibration,
        double tagSizeMillimeters,
        bool approximate)
    {
        Point2f center = new(
            corners.Average(static point => point.X),
            corners.Average(static point => point.Y));
        double perimeter = PolygonPerimeter(corners);
        double area = Math.Abs(PolygonSignedArea(corners));

        // IPPE_SQUARE 要求顺序：左上、右上、右下、左下；标记坐标原点位于中心。
        float half = (float)(tagSizeMillimeters / 2d);
        Point3f[] objectPoints =
        [
            new(-half, half, 0),
            new(half, half, 0),
            new(half, -half, 0),
            new(-half, -half, 0)
        ];

        PoseSolution? pose = ChooseBestPose(objectPoints, corners, calibration);
        if (pose is null || pose.ReprojectionError > MaximumAcceptedReprojectionErrorPixels)
        {
            string failureLabel = pose is null
                ? "pose failed"
                : $"pose rejected RMS={pose.ReprojectionError:F1}px";
            DrawCenterAndLabel(annotated, id, center, null, approximate, failureLabel);
            return new TagPoseResult
            {
                Id = id,
                Corners = corners.ToArray(),
                Center = center,
                RotationVector = [],
                TranslationMillimeters = [],
                DistanceMillimeters = double.NaN,
                ReprojectionErrorPixels = pose?.ReprojectionError ?? double.NaN,
                PerimeterPixels = perimeter,
                AreaPixels = area,
                PoseUsesApproximateIntrinsics = approximate
            };
        }

        double[] rotationVector = pose.RotationVector;
        double[] translationVector = pose.TranslationVector;

        Cv2.Rodrigues(rotationVector, out double[,] rotationMatrix, out _);
        EulerAngles euler = ToEulerDegrees(rotationMatrix);
        QuaternionRotation quaternion = ToQuaternion(rotationMatrix);
        double distance = Math.Sqrt(
            translationVector[0] * translationVector[0] +
            translationVector[1] * translationVector[1] +
            translationVector[2] * translationVector[2]);

        double reprojectionError = pose.ReprojectionError;

        DrawAxes(annotated, rotationVector, translationVector, calibration, tagSizeMillimeters * 0.6);
        DrawCenterAndLabel(annotated, id, center, translationVector, approximate);

        return new TagPoseResult
        {
            Id = id,
            Corners = corners.ToArray(),
            Center = center,
            RotationVector = rotationVector,
            TranslationMillimeters = translationVector,
            RotationMatrix = rotationMatrix,
            EulerDegrees = euler,
            Quaternion = quaternion,
            DistanceMillimeters = distance,
            ReprojectionErrorPixels = reprojectionError,
            PerimeterPixels = perimeter,
            AreaPixels = area,
            PoseUsesApproximateIntrinsics = approximate
        };
    }

    private static PoseSolution? ChooseBestPose(
        Point3f[] objectPoints,
        Point2f[] imagePoints,
        CameraCalibration calibration)
    {
        PoseSolution? ippe = TrySolvePose(
            objectPoints, imagePoints, calibration, SolvePnPMethod.IPPE_SQUARE);
        PoseSolution? iterative = TrySolvePose(
            objectPoints, imagePoints, calibration, SolvePnPMethod.Iterative);

        if (ippe is null) return iterative;
        if (iterative is null) return ippe;
        return iterative.ReprojectionError + 1e-6 < ippe.ReprojectionError ? iterative : ippe;
    }

    private static PoseSolution? TrySolvePose(
        Point3f[] objectPoints,
        Point2f[] imagePoints,
        CameraCalibration calibration,
        SolvePnPMethod method)
    {
        try
        {
            using Mat objectPointMatrix = Mat.FromArray(objectPoints);
            using Mat imagePointMatrix = Mat.FromArray(imagePoints);
            using Mat cameraMatrix = Mat.FromArray(calibration.CameraMatrix);
            using Mat distortion = Mat.FromArray(calibration.DistortionCoefficients);
            using var rotationOutput = new Mat();
            using var translationOutput = new Mat();
            Cv2.SolvePnP(
                objectPointMatrix,
                imagePointMatrix,
                cameraMatrix,
                distortion,
                rotationOutput,
                translationOutput,
                false,
                method);
            if (rotationOutput.Empty() || translationOutput.Empty()) return null;
            rotationOutput.GetArray(out double[] rotationVector);
            translationOutput.GetArray(out double[] translationVector);
            if (rotationVector.Length < 3 || translationVector.Length < 3 ||
                rotationVector.Any(static value => !double.IsFinite(value)) ||
                translationVector.Any(static value => !double.IsFinite(value)) ||
                translationVector[2] <= 0) return null;

            Cv2.Rodrigues(rotationVector, out double[,] rotationMatrix, out _);
            if (rotationMatrix[2, 2] >= 0 || objectPoints.Any(point =>
                    rotationMatrix[2, 0] * point.X +
                    rotationMatrix[2, 1] * point.Y +
                    translationVector[2] <= 0)) return null;

            Cv2.ProjectPoints(
                objectPoints,
                rotationVector,
                translationVector,
                calibration.CameraMatrix,
                calibration.DistortionCoefficients,
                out Point2f[] projected,
                out _,
                0d);
            double error = RootMeanSquareError(imagePoints, projected);
            return double.IsFinite(error)
                ? new PoseSolution(rotationVector, translationVector, error)
                : null;
        }
        catch (OpenCVException)
        {
            return null;
        }
    }

    private static void DrawAxes(
        Mat image,
        double[] rotationVector,
        double[] translationVector,
        CameraCalibration calibration,
        double length)
    {
        float axis = (float)length;
        Point3f[] axisPoints =
        [
            new(0, 0, 0),
            new(axis, 0, 0),
            new(0, axis, 0),
            new(0, 0, axis)
        ];
        Cv2.ProjectPoints(
            axisPoints,
            rotationVector,
            translationVector,
            calibration.CameraMatrix,
            calibration.DistortionCoefficients,
            out Point2f[] imagePoints,
            out _,
            0d);
        if (imagePoints.Length != 4 || imagePoints.Any(static p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return;

        CvPoint origin = imagePoints[0].ToPoint();
        CvPoint x = imagePoints[1].ToPoint();
        CvPoint y = imagePoints[2].ToPoint();
        CvPoint z = imagePoints[3].ToPoint();
        Cv2.Line(image, origin, x, new Scalar(0, 0, 255), 3, LineTypes.AntiAlias);
        Cv2.Line(image, origin, y, new Scalar(0, 255, 0), 3, LineTypes.AntiAlias);
        Cv2.Line(image, origin, z, new Scalar(255, 80, 0), 3, LineTypes.AntiAlias);
        Cv2.PutText(image, "X", x, HersheyFonts.HersheySimplex, 0.55, new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "Y", y, HersheyFonts.HersheySimplex, 0.55, new Scalar(0, 255, 0), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "Z", z, HersheyFonts.HersheySimplex, 0.55, new Scalar(255, 80, 0), 2, LineTypes.AntiAlias);
    }

    private static void DrawRejectedCandidates(
        Mat image,
        IEnumerable<Point2f[]> rejected,
        string dictionaryShortName)
    {
        double minimumArea = Math.Pow(Math.Max(image.Width, image.Height) * 0.018, 2);
        Point2f[][] candidates = rejected
            .Where(static points => points.Length == 4)
            .Where(IsSquareLike)
            .Select(points => new { Points = points, Area = Math.Abs(PolygonSignedArea(points)) })
            .Where(item => item.Area >= minimumArea)
            .OrderByDescending(static item => item.Area)
            .Take(20)
            .Select(static item => item.Points)
            .ToArray();

        foreach (Point2f[] candidate in candidates)
        {
            CvPoint[] polygon = candidate.Select(static point => point.ToPoint()).ToArray();
            Cv2.Polylines(image, [polygon], true, new Scalar(0, 110, 255), 3, LineTypes.AntiAlias);
            CvPoint center = new(
                (int)Math.Round(candidate.Average(static point => point.X)),
                (int)Math.Round(candidate.Average(static point => point.Y)));
            Cv2.DrawMarker(image, center, new Scalar(0, 80, 255), MarkerTypes.TiltedCross, 26, 3, LineTypes.AntiAlias);
            Cv2.PutText(image, $"INVALID {dictionaryShortName}", new CvPoint(Math.Max(0, center.X - 110), Math.Max(22, center.Y - 18)),
                HersheyFonts.HersheySimplex, 0.55, new Scalar(0, 100, 255), 2, LineTypes.AntiAlias);
        }

        string message = $"NO VALID {dictionaryShortName} - {candidates.Length} large candidates rejected";
        Cv2.Rectangle(image, new Rect(0, 0, Math.Min(image.Width, 900), 54), new Scalar(20, 20, 20), -1);
        Cv2.PutText(image, message, new CvPoint(16, 37), HersheyFonts.HersheySimplex, 0.8,
            new Scalar(0, 130, 255), 2, LineTypes.AntiAlias);
    }

    private static bool IsSquareLike(Point2f[] points)
    {
        double[] edges = new double[4];
        for (int index = 0; index < 4; index++)
        {
            Point2f a = points[index];
            Point2f b = points[(index + 1) % 4];
            edges[index] = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        }
        double shortest = edges.Min();
        if (shortest < 1 || edges.Max() / shortest > 1.8) return false;

        for (int index = 0; index < 4; index++)
        {
            Point2f center = points[index];
            Point2f previous = points[(index + 3) % 4];
            Point2f next = points[(index + 1) % 4];
            double ax = previous.X - center.X;
            double ay = previous.Y - center.Y;
            double bx = next.X - center.X;
            double by = next.Y - center.Y;
            double cosine = Math.Abs((ax * bx + ay * by) /
                (Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by)));
            if (!double.IsFinite(cosine) || cosine > 0.6) return false;
        }
        return true;
    }

    private static void DrawCenterAndLabel(
        Mat image,
        int id,
        Point2f center,
        double[]? translation,
        bool approximate,
        string? failureLabel = null)
    {
        CvPoint centerPixel = center.ToPoint();
        Cv2.DrawMarker(image, centerPixel, new Scalar(255, 255, 0), MarkerTypes.Cross, 18, 2, LineTypes.AntiAlias);
        string position = translation is { Length: >= 3 }
            ? $" T=({translation[0]:F1},{translation[1]:F1},{translation[2]:F1})mm"
            : $" {failureLabel ?? "pose failed"}";
        string label = $"ID {id}{position}{(approximate ? " [APPROX]" : string.Empty)}";
        CvPoint labelPoint = new(Math.Max(0, centerPixel.X - 100), Math.Max(20, centerPixel.Y - 18));
        Scalar color = translation is null
            ? new Scalar(0, 80, 255)
            : approximate ? new Scalar(0, 190, 255) : new Scalar(60, 255, 60);
        Cv2.PutText(image, label, labelPoint, HersheyFonts.HersheySimplex, 0.52,
            color, 2, LineTypes.AntiAlias);
    }

    private static EulerAngles ToEulerDegrees(double[,] rotation)
    {
        double sy = Math.Sqrt(rotation[0, 0] * rotation[0, 0] + rotation[1, 0] * rotation[1, 0]);
        bool singular = sy < 1e-8;
        double roll;
        double pitch;
        double yaw;
        if (!singular)
        {
            roll = Math.Atan2(rotation[2, 1], rotation[2, 2]);
            pitch = Math.Atan2(-rotation[2, 0], sy);
            yaw = Math.Atan2(rotation[1, 0], rotation[0, 0]);
        }
        else
        {
            roll = Math.Atan2(-rotation[1, 2], rotation[1, 1]);
            pitch = Math.Atan2(-rotation[2, 0], sy);
            yaw = 0d;
        }

        const double radiansToDegrees = 180d / Math.PI;
        return new EulerAngles
        {
            Roll = roll * radiansToDegrees,
            Pitch = pitch * radiansToDegrees,
            Yaw = yaw * radiansToDegrees
        };
    }

    private static QuaternionRotation ToQuaternion(double[,] r)
    {
        double x;
        double y;
        double z;
        double w;
        double trace = r[0, 0] + r[1, 1] + r[2, 2];
        if (trace > 0)
        {
            double s = Math.Sqrt(trace + 1d) * 2d;
            w = 0.25d * s;
            x = (r[2, 1] - r[1, 2]) / s;
            y = (r[0, 2] - r[2, 0]) / s;
            z = (r[1, 0] - r[0, 1]) / s;
        }
        else if (r[0, 0] > r[1, 1] && r[0, 0] > r[2, 2])
        {
            double s = Math.Sqrt(1d + r[0, 0] - r[1, 1] - r[2, 2]) * 2d;
            w = (r[2, 1] - r[1, 2]) / s;
            x = 0.25d * s;
            y = (r[0, 1] + r[1, 0]) / s;
            z = (r[0, 2] + r[2, 0]) / s;
        }
        else if (r[1, 1] > r[2, 2])
        {
            double s = Math.Sqrt(1d + r[1, 1] - r[0, 0] - r[2, 2]) * 2d;
            w = (r[0, 2] - r[2, 0]) / s;
            x = (r[0, 1] + r[1, 0]) / s;
            y = 0.25d * s;
            z = (r[1, 2] + r[2, 1]) / s;
        }
        else
        {
            double s = Math.Sqrt(1d + r[2, 2] - r[0, 0] - r[1, 1]) * 2d;
            w = (r[1, 0] - r[0, 1]) / s;
            x = (r[0, 2] + r[2, 0]) / s;
            y = (r[1, 2] + r[2, 1]) / s;
            z = 0.25d * s;
        }

        return new QuaternionRotation { X = x, Y = y, Z = z, W = w };
    }

    private static double RootMeanSquareError(IReadOnlyList<Point2f> actual, IReadOnlyList<Point2f> projected)
    {
        int count = Math.Min(actual.Count, projected.Count);
        if (count == 0) return double.NaN;
        double sumSquared = 0d;
        for (int index = 0; index < count; index++)
        {
            double dx = actual[index].X - projected[index].X;
            double dy = actual[index].Y - projected[index].Y;
            sumSquared += dx * dx + dy * dy;
        }
        return Math.Sqrt(sumSquared / count);
    }

    private static double PolygonPerimeter(IReadOnlyList<Point2f> points)
    {
        double total = 0d;
        for (int index = 0; index < points.Count; index++)
        {
            Point2f a = points[index];
            Point2f b = points[(index + 1) % points.Count];
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            total += Math.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }

    private static double PolygonSignedArea(IReadOnlyList<Point2f> points)
    {
        double twiceArea = 0d;
        for (int index = 0; index < points.Count; index++)
        {
            Point2f a = points[index];
            Point2f b = points[(index + 1) % points.Count];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return twiceArea / 2d;
    }

    private static Mat ToGray8(Mat source)
    {
        using Mat eightBit = To8Bit(source);
        var gray = new Mat();
        if (eightBit.Channels() == 1) eightBit.CopyTo(gray);
        else if (eightBit.Channels() == 3) Cv2.CvtColor(eightBit, gray, ColorConversionCodes.BGR2GRAY);
        else if (eightBit.Channels() == 4) Cv2.CvtColor(eightBit, gray, ColorConversionCodes.BGRA2GRAY);
        else throw new NotSupportedException($"不支持 {source.Channels()} 通道图像。");
        return gray;
    }

    private static Mat ToBgr8(Mat source)
    {
        using Mat eightBit = To8Bit(source);
        var bgr = new Mat();
        if (eightBit.Channels() == 3) eightBit.CopyTo(bgr);
        else if (eightBit.Channels() == 1) Cv2.CvtColor(eightBit, bgr, ColorConversionCodes.GRAY2BGR);
        else if (eightBit.Channels() == 4) Cv2.CvtColor(eightBit, bgr, ColorConversionCodes.BGRA2BGR);
        else throw new NotSupportedException($"不支持 {source.Channels()} 通道图像。");
        return bgr;
    }

    private static Mat To8Bit(Mat source)
    {
        var converted = new Mat();
        int depth = source.Depth();
        if (depth == MatType.CV_8U)
        {
            source.CopyTo(converted);
        }
        else if (depth == MatType.CV_8S)
        {
            source.ConvertTo(converted, MatType.CV_8U, 1d, 128d);
        }
        else if (depth == MatType.CV_16U)
        {
            source.ConvertTo(converted, MatType.CV_8U, 1d / 256d);
        }
        else if (depth == MatType.CV_16S)
        {
            source.ConvertTo(converted, MatType.CV_8U, 1d / 256d, 128d);
        }
        else
        {
            Cv2.Normalize(source, converted, 0d, 255d, NormTypes.MinMax, MatType.CV_8U);
        }

        return converted;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _detector.Dispose();
            _dictionary.Dispose();
        }
    }

    private sealed record PoseSolution(
        double[] RotationVector,
        double[] TranslationVector,
        double ReprojectionError);
}
