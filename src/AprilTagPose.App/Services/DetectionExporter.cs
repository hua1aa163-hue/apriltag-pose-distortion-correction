using System.Globalization;
using System.Text;
using System.Text.Json;
using AprilTagPose.Models;

namespace AprilTagPose.Services;

public static class DetectionExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void SaveJson(string path, DetectionFrameResult result, string? sourceName, double tagSizeMillimeters)
    {
        var payload = new
        {
            schemaVersion = 1,
            dictionary = result.Dictionary.DisplayName(),
            source = sourceName,
            capturedAt = result.CapturedAt,
            processingMilliseconds = result.ProcessingTime.TotalMilliseconds,
            tagSizeMillimeters,
            approximateIntrinsics = result.UsesApproximateIntrinsics,
            calibration = result.CalibrationUsed,
            rejectedCandidateCount = result.RejectedCandidateCount,
            tags = result.Tags.Select(tag =>
            {
                bool hasPose = tag.RotationVector.Length >= 3 && tag.TranslationMillimeters.Length >= 3;
                return new
                {
                    tag.Id,
                    poseValid = hasPose,
                    centerPixels = new { x = tag.Center.X, y = tag.Center.Y },
                    cornersPixels = tag.Corners.Select(point => new { x = point.X, y = point.Y }),
                    translationMillimeters = VectorObject(tag.TranslationMillimeters),
                    rotationVector = VectorObject(tag.RotationVector),
                    rotationMatrix = hasPose ? MatrixRows(tag.RotationMatrix) : null,
                    eulerDegreesZYX = hasPose
                        ? new { tag.EulerDegrees.Roll, tag.EulerDegrees.Pitch, tag.EulerDegrees.Yaw }
                        : null,
                    quaternionXYZW = hasPose
                        ? new { tag.Quaternion.X, tag.Quaternion.Y, tag.Quaternion.Z, tag.Quaternion.W }
                        : null,
                    distanceMillimeters = FiniteOrNull(tag.DistanceMillimeters),
                    reprojectionErrorPixels = FiniteOrNull(tag.ReprojectionErrorPixels),
                    tag.PerimeterPixels,
                    tag.AreaPixels,
                    tag.PoseUsesApproximateIntrinsics
                };
            })
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions), new UTF8Encoding(false));
    }

    public static void SaveCsv(string path, DetectionFrameResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("dictionary,id,pose_valid,center_x_px,center_y_px,tx_mm,ty_mm,tz_mm,distance_mm,roll_deg,pitch_deg,yaw_deg,reprojection_error_px,perimeter_px,area_px,approximate_intrinsics");
        foreach (TagPoseResult tag in result.Tags)
        {
            double[] t = tag.TranslationMillimeters;
            bool hasPose = t.Length >= 3 && tag.RotationVector.Length >= 3;
            builder.Append(result.Dictionary.ShortName()).Append(',')
                .Append(tag.Id).Append(',').Append(hasPose ? "true" : "false").Append(',')
                .Append(F(tag.Center.X)).Append(',').Append(F(tag.Center.Y)).Append(',')
                .Append(hasPose ? F(t[0]) : string.Empty).Append(',')
                .Append(hasPose ? F(t[1]) : string.Empty).Append(',')
                .Append(hasPose ? F(t[2]) : string.Empty).Append(',')
                .Append(hasPose ? F(tag.DistanceMillimeters) : string.Empty).Append(',')
                .Append(hasPose ? F(tag.EulerDegrees.Roll) : string.Empty).Append(',')
                .Append(hasPose ? F(tag.EulerDegrees.Pitch) : string.Empty).Append(',')
                .Append(hasPose ? F(tag.EulerDegrees.Yaw) : string.Empty).Append(',')
                .Append(F(tag.ReprojectionErrorPixels)).Append(',')
                .Append(F(tag.PerimeterPixels)).Append(',').Append(F(tag.AreaPixels)).Append(',')
                .AppendLine(tag.PoseUsesApproximateIntrinsics ? "true" : "false");
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    private static object? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;

    private static object? VectorObject(double[] values) => values.Length >= 3
        ? new { x = values[0], y = values[1], z = values[2] }
        : null;

    private static double[][] MatrixRows(double[,] matrix) =>
    [
        [matrix[0, 0], matrix[0, 1], matrix[0, 2]],
        [matrix[1, 0], matrix[1, 1], matrix[1, 2]],
        [matrix[2, 0], matrix[2, 1], matrix[2, 2]]
    ];

    private static string F(double value) => double.IsFinite(value)
        ? value.ToString("0.######", CultureInfo.InvariantCulture)
        : string.Empty;
}
