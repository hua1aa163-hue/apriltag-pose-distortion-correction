using OpenCvSharp;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DistortionCorrection.Camera;

public sealed record LensCalibrationResult(
    LensCalibration Calibration,
    IReadOnlyList<string> AcceptedImages,
    IReadOnlyList<string> RejectedImages);

/// <summary>使用 OpenCvSharp 完成棋盘格标定及相机帧镜头去畸变。</summary>
public sealed class OpenCvLensCorrectionService : IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"
    };

    private LensCalibration? _calibration;
    private double _alpha;
    private Mat? _mapX;
    private Mat? _mapY;

    public bool Enabled => _calibration is not null;

    public void Configure(LensCalibration? calibration, double alpha)
    {
        if (alpha is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha 必须在 0 到 1 之间。");
        calibration?.Validate();
        ClearMaps();
        _calibration = calibration;
        _alpha = alpha;
    }

    public Bitmap Correct(Bitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        LensCalibration calibration = _calibration
            ?? throw new InvalidOperationException("尚未配置镜头标定参数。");
        if (source.Width != calibration.ImageWidth || source.Height != calibration.ImageHeight)
        {
            throw new InvalidDataException(
                $"相机帧尺寸 {source.Width}×{source.Height} 与镜头标定尺寸 " +
                $"{calibration.ImageWidth}×{calibration.ImageHeight} 不一致。请使用当前相机分辨率重新标定镜头。");
        }

        EnsureMaps(calibration);
        using Mat input = BitmapToMat(source);
        using var output = new Mat();
        Cv2.Remap(input, output, _mapX!, _mapY!, InterpolationFlags.Linear,
            BorderTypes.Constant, Scalar.Black);
        return MatToBitmap(output);
    }

    public static LensCalibrationResult CalibrateFromChessboardFolder(
        string imageDirectory,
        int chessboardColumns,
        int chessboardRows,
        double squareSize,
        string? cameraSerial = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(imageDirectory))
            throw new DirectoryNotFoundException("棋盘格图片文件夹不存在：" + imageDirectory);
        if (chessboardColumns < 3 || chessboardRows < 3)
            throw new ArgumentOutOfRangeException(nameof(chessboardColumns), "棋盘格内角点行列数均不能小于 3。");
        if (squareSize <= 0) throw new ArgumentOutOfRangeException(nameof(squareSize), "方格边长必须大于 0。");

        string[] files = Directory.EnumerateFiles(imageDirectory)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) throw new InvalidOperationException("文件夹中没有可用的棋盘格图片。");

        var imagePoints = new List<IEnumerable<Point2f>>();
        var accepted = new List<string>();
        var rejected = new List<string>();
        var patternSize = new OpenCvSharp.Size(chessboardColumns, chessboardRows);
        OpenCvSharp.Size? imageSize = null;

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using Mat gray = Cv2.ImRead(file, ImreadModes.Grayscale);
                if (gray.Empty())
                {
                    rejected.Add($"{file} | 无法读取");
                    continue;
                }

                var currentSize = new OpenCvSharp.Size(gray.Width, gray.Height);
                if (imageSize is not null && currentSize != imageSize.Value)
                {
                    rejected.Add($"{file} | 尺寸 {gray.Width}×{gray.Height} 不一致");
                    continue;
                }

                bool found = Cv2.FindChessboardCorners(gray, patternSize, out Point2f[] corners,
                    ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage);
                if (!found || corners.Length != chessboardColumns * chessboardRows)
                {
                    rejected.Add($"{file} | 未识别完整内角点");
                    continue;
                }

                Cv2.CornerSubPix(gray, corners, new OpenCvSharp.Size(11, 11),
                    new OpenCvSharp.Size(-1, -1),
                    new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 40, 0.001));
                imageSize ??= currentSize;
                imagePoints.Add(corners);
                accepted.Add(file);
            }
            catch (OpenCVException exception)
            {
                rejected.Add($"{file} | OpenCV：{exception.Message}");
            }
        }

        if (imagePoints.Count < 5)
        {
            throw new InvalidOperationException(
                $"仅识别出 {imagePoints.Count} 张有效棋盘格图片，至少需要 5 张；建议使用 12–20 张不同角度和位置的图片。");
        }

        Point3f[] objectGrid = CreateObjectGrid(chessboardColumns, chessboardRows, squareSize);
        IEnumerable<IEnumerable<Point3f>> objectPoints = Enumerable.Range(0, imagePoints.Count)
            .Select(_ => (IEnumerable<Point3f>)objectGrid);
        var cameraMatrix = new double[3, 3];
        var distortion = new double[5];
        double rms = Cv2.CalibrateCamera(
            objectPoints, imagePoints, imageSize!.Value, cameraMatrix, distortion,
            out Vec3d[] rotationVectors, out Vec3d[] translationVectors,
            CalibrationFlags.None,
            new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 100, 1e-9));

        double meanError = CalculateMeanReprojectionError(
            objectGrid, imagePoints, cameraMatrix, distortion, rotationVectors, translationVectors);
        var calibration = new LensCalibration
        {
            ImageWidth = imageSize.Value.Width,
            ImageHeight = imageSize.Value.Height,
            CameraMatrix = Flatten(cameraMatrix),
            DistortionCoefficients = distortion,
            RmsReprojectionError = rms,
            MeanReprojectionError = meanError,
            UsedImageCount = accepted.Count,
            RejectedImageCount = rejected.Count,
            ChessboardColumns = chessboardColumns,
            ChessboardRows = chessboardRows,
            ChessboardSquareSize = squareSize,
            CameraSerial = cameraSerial ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            AcceptedImages = accepted.ToArray(),
            RejectedImages = rejected.ToArray()
        };
        calibration.Validate();
        return new LensCalibrationResult(calibration, accepted, rejected);
    }

    private void EnsureMaps(LensCalibration calibration)
    {
        if (_mapX is not null && _mapY is not null) return;
        var size = new OpenCvSharp.Size(calibration.ImageWidth, calibration.ImageHeight);
        double[,] cameraMatrix = calibration.ToCameraMatrix();
        double[,] optimalMatrix = Cv2.GetOptimalNewCameraMatrix(
            cameraMatrix, calibration.DistortionCoefficients, size, _alpha, size,
            out _, centerPrincipalPoint: false)
            ?? throw new OpenCVException("OpenCV 未返回有效的新相机内参矩阵。");
        using Mat cameraMatrixMat = Mat.FromArray(cameraMatrix);
        using Mat distortionMat = Mat.FromArray(calibration.DistortionCoefficients);
        using Mat optimalMatrixMat = Mat.FromArray(optimalMatrix);
        using var rectification = new Mat();
        _mapX = new Mat();
        _mapY = new Mat();
        Cv2.InitUndistortRectifyMap(cameraMatrixMat, distortionMat,
            rectification, optimalMatrixMat,
            size, MatType.CV_32FC1, _mapX, _mapY);
    }

    private static Point3f[] CreateObjectGrid(int columns, int rows, double squareSize)
    {
        var points = new Point3f[columns * rows];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
                points[row * columns + column] = new Point3f(
                    (float)(column * squareSize), (float)(row * squareSize), 0);
        }
        return points;
    }

    private static double CalculateMeanReprojectionError(
        Point3f[] objectGrid,
        IReadOnlyList<IEnumerable<Point2f>> imagePoints,
        double[,] cameraMatrix,
        double[] distortion,
        IReadOnlyList<Vec3d> rotationVectors,
        IReadOnlyList<Vec3d> translationVectors)
    {
        double sum = 0;
        int count = 0;
        for (int view = 0; view < imagePoints.Count; view++)
        {
            Vec3d rotation = rotationVectors[view];
            Vec3d translation = translationVectors[view];
            Cv2.ProjectPoints(objectGrid,
                [rotation.Item0, rotation.Item1, rotation.Item2],
                [translation.Item0, translation.Item1, translation.Item2],
                cameraMatrix, distortion, out Point2f[] projected, out _);
            Point2f[] detected = imagePoints[view].ToArray();
            for (int index = 0; index < detected.Length; index++)
            {
                double dx = detected[index].X - projected[index].X;
                double dy = detected[index].Y - projected[index].Y;
                sum += Math.Sqrt(dx * dx + dy * dy);
                count++;
            }
        }
        return count == 0 ? 0 : sum / count;
    }

    private static double[] Flatten(double[,] matrix) =>
    [
        matrix[0, 0], matrix[0, 1], matrix[0, 2],
        matrix[1, 0], matrix[1, 1], matrix[1, 2],
        matrix[2, 0], matrix[2, 1], matrix[2, 2]
    ];

    private static Mat BitmapToMat(Bitmap source)
    {
        using var bgr = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(bgr))
        {
            graphics.Clear(Color.Black);
            graphics.DrawImageUnscaled(source, 0, 0);
        }
        Rectangle rectangle = new(0, 0, bgr.Width, bgr.Height);
        BitmapData data = bgr.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            using Mat wrapped = Mat.FromPixelData(
                bgr.Height, bgr.Width, MatType.CV_8UC3, data.Scan0, data.Stride);
            return wrapped.Clone();
        }
        finally
        {
            bgr.UnlockBits(data);
        }
    }

    private static Bitmap MatToBitmap(Mat source)
    {
        using Mat bgr = source.Channels() switch
        {
            3 => source.Clone(),
            4 => ConvertColor(source, ColorConversionCodes.BGRA2BGR),
            1 => ConvertColor(source, ColorConversionCodes.GRAY2BGR),
            _ => throw new InvalidDataException($"不支持的 OpenCV 图像通道数：{source.Channels()}。")
        };

        var bitmap = new Bitmap(bgr.Width, bgr.Height, PixelFormat.Format24bppRgb);
        Rectangle rectangle = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            int rowBytes = checked(bgr.Width * 3);
            var row = new byte[rowBytes];
            int height = bgr.Height;
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(bgr.Ptr(y), row, 0, rowBytes);
                Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private static Mat ConvertColor(Mat source, ColorConversionCodes code)
    {
        var result = new Mat();
        Cv2.CvtColor(source, result, code);
        return result;
    }

    private void ClearMaps()
    {
        _mapX?.Dispose();
        _mapY?.Dispose();
        _mapX = null;
        _mapY = null;
    }

    public void Dispose()
    {
        ClearMaps();
        _calibration = null;
    }
}
