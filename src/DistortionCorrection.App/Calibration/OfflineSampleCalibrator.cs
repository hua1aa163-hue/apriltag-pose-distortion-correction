using DistortionCorrection.Imaging;
using DistortionCorrection.Models;
using System.Drawing.Imaging;

namespace DistortionCorrection.Calibration;

public sealed record OfflineCalibrationResult(
    MeshCalibration Calibration,
    PointGridDetectionResult ProjectorDetection,
    PointGridDetectionResult CameraDetection,
    string CalibrationPath,
    string CorrectedPatternPath,
    string ProjectorOverlayPath,
    string CameraOverlayPath);

public static class OfflineSampleCalibrator
{
    public static OfflineCalibrationResult Analyze(
        string projectorPatternPath,
        string cameraCapturePath,
        string outputDirectory,
        AppSettings settings,
        int threshold = 0)
    {
        settings.Validate();
        Directory.CreateDirectory(outputDirectory);
        using var projectorImage = new Bitmap(projectorPatternPath);
        using var cameraImage = new Bitmap(cameraCapturePath);
        PointGridDetectionResult projectorDetection = PointGridDetector.Detect(
            projectorImage, settings.GridColumns, settings.GridRows, threshold);
        PointGridDetectionResult cameraDetection = PointGridDetector.Detect(
            cameraImage, settings.GridColumns, settings.GridRows, threshold);

        PointF[] desired = BuildDesiredCameraPoints(cameraDetection.OrderedPoints, settings);
        PointF[] updatedProjector = UpdateProjectorPoints(
            projectorDetection.OrderedPoints, cameraDetection.OrderedPoints, desired,
            settings.GridColumns, settings.GridRows, settings.CanvasRectangle, settings.LoopRelaxation);
        PointF[] source = PatternGenerator.CreateRegularGridPoints(settings);
        double rms = CalculateRms(cameraDetection.OrderedPoints, desired);
        var calibration = new MeshCalibration
        {
            CameraSerial = settings.PreferredCameraSerial,
            CanvasWidth = settings.CanvasWidth,
            CanvasHeight = settings.CanvasHeight,
            SourceContentWidth = settings.SourceContentWidth,
            SourceContentHeight = settings.SourceContentHeight,
            ExpectedInverseMappedWidth = settings.ExpectedInverseMappedWidth,
            ExpectedInverseMappedHeight = settings.ExpectedInverseMappedHeight,
            Columns = settings.GridColumns,
            Rows = settings.GridRows,
            SourcePoints = source,
            ProjectorPoints = updatedProjector,
            DesiredCameraPoints = desired,
            LastObservedCameraPoints = cameraDetection.OrderedPoints,
            LastRmsErrorPixels = rms
        };

        string calibrationPath = Path.Combine(outputDirectory, "sample-calibration.json");
        string correctedPatternPath = Path.Combine(outputDirectory,
            $"first-corrected-{settings.GridColumns}x{settings.GridRows}.png");
        string projectorOverlayPath = Path.Combine(outputDirectory, "projector-detection-overlay.png");
        string cameraOverlayPath = Path.Combine(outputDirectory, "camera-detection-overlay.png");
        CalibrationStorage.SaveMesh(calibration, calibrationPath);
        using (Bitmap corrected = PatternGenerator.CreateWarpedDotPattern(settings, calibration))
        {
            corrected.Save(correctedPatternPath, ImageFormat.Png);
        }
        using (Bitmap overlay = PointGridDetector.DrawOverlay(projectorImage, projectorDetection))
        {
            overlay.Save(projectorOverlayPath, ImageFormat.Png);
        }
        using (Bitmap overlay = PointGridDetector.DrawOverlay(cameraImage, cameraDetection))
        {
            overlay.Save(cameraOverlayPath, ImageFormat.Png);
        }

        return new OfflineCalibrationResult(
            calibration, projectorDetection, cameraDetection, calibrationPath, correctedPatternPath,
            projectorOverlayPath, cameraOverlayPath);
    }

    private static PointF[] BuildDesiredCameraPoints(IReadOnlyList<PointF> observed, AppSettings settings)
    {
        float minX = observed.Min(point => point.X);
        float maxX = observed.Max(point => point.X);
        float minY = observed.Min(point => point.Y);
        float maxY = observed.Max(point => point.Y);
        double aspect = settings.CanvasWidth / (double)settings.CanvasHeight;
        double width = Math.Min(maxX - minX, (maxY - minY) * aspect) * 0.98;
        double height = width / aspect;
        double left = (minX + maxX - width) / 2;
        double top = (minY + maxY - height) / 2;
        var desired = new PointF[settings.GridColumns * settings.GridRows];
        for (int row = 0; row < settings.GridRows; row++)
        {
            float y = (float)(top + height * row / (settings.GridRows - 1));
            for (int column = 0; column < settings.GridColumns; column++)
            {
                float x = (float)(left + width * column / (settings.GridColumns - 1));
                desired[row * settings.GridColumns + column] = new PointF(x, y);
            }
        }
        return desired;
    }

    private static PointF[] UpdateProjectorPoints(
        IReadOnlyList<PointF> projector,
        IReadOnlyList<PointF> observed,
        IReadOnlyList<PointF> desired,
        int columns,
        int rows,
        Rectangle clip,
        double relaxation)
    {
        var result = projector.ToArray();
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                int left = row * columns + Math.Max(0, column - 1);
                int right = row * columns + Math.Min(columns - 1, column + 1);
                int top = Math.Max(0, row - 1) * columns + column;
                int bottom = Math.Min(rows - 1, row + 1) * columns + column;
                PointF pCol = Subtract(projector[right], projector[left]);
                PointF pRow = Subtract(projector[bottom], projector[top]);
                PointF cCol = Subtract(observed[right], observed[left]);
                PointF cRow = Subtract(observed[bottom], observed[top]);
                if (!TryPhysicalJacobian(pCol, pRow, cCol, cRow,
                    out double j11, out double j12, out double j21, out double j22)) continue;
                double determinant = j11 * j22 - j12 * j21;
                if (Math.Abs(determinant) < 1e-8) continue;
                double ex = desired[index].X - observed[index].X;
                double ey = desired[index].Y - observed[index].Y;
                double dx = relaxation * (j22 * ex - j12 * ey) / determinant;
                double dy = relaxation * (-j21 * ex + j11 * ey) / determinant;
                double magnitude = Math.Sqrt(dx * dx + dy * dy);
                if (magnitude > 80)
                {
                    dx *= 80 / magnitude;
                    dy *= 80 / magnitude;
                }
                result[index] = new PointF(
                    (float)Math.Clamp(projector[index].X + dx, clip.Left, clip.Right - 1),
                    (float)Math.Clamp(projector[index].Y + dy, clip.Top, clip.Bottom - 1));
            }
        }
        return result;
    }

    private static bool TryPhysicalJacobian(
        PointF projectorColumn,
        PointF projectorRow,
        PointF cameraColumn,
        PointF cameraRow,
        out double j11,
        out double j12,
        out double j21,
        out double j22)
    {
        double determinant = projectorColumn.X * projectorRow.Y - projectorRow.X * projectorColumn.Y;
        if (Math.Abs(determinant) < 1e-8)
        {
            j11 = j12 = j21 = j22 = 0;
            return false;
        }
        // J = [cameraColumn cameraRow] * inv([projectorColumn projectorRow])
        j11 = (cameraColumn.X * projectorRow.Y - cameraRow.X * projectorColumn.Y) / determinant;
        j12 = (-cameraColumn.X * projectorRow.X + cameraRow.X * projectorColumn.X) / determinant;
        j21 = (cameraColumn.Y * projectorRow.Y - cameraRow.Y * projectorColumn.Y) / determinant;
        j22 = (-cameraColumn.Y * projectorRow.X + cameraRow.Y * projectorColumn.X) / determinant;
        return true;
    }

    private static PointF Subtract(PointF a, PointF b) => new(a.X - b.X, a.Y - b.Y);

    private static double CalculateRms(IReadOnlyList<PointF> observed, IReadOnlyList<PointF> desired)
    {
        double sum = 0;
        for (int i = 0; i < observed.Count; i++)
        {
            double dx = observed[i].X - desired[i].X;
            double dy = observed[i].Y - desired[i].Y;
            sum += dx * dx + dy * dy;
        }
        return Math.Sqrt(sum / observed.Count);
    }
}
