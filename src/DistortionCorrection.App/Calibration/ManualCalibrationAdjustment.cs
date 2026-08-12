using DistortionCorrection.Imaging;
using DistortionCorrection.Models;

namespace DistortionCorrection.Calibration;

public sealed record ManualAdjustmentResult(
    MeshCalibration Calibration,
    MeshGeometryDiagnostics Geometry);

/// <summary>把人眼确认的旋转、透视、缩放和平移统一作用到标定投影网格。</summary>
public static class ManualCalibrationAdjustment
{
    public static ManualAdjustmentResult Apply(
        MeshCalibration source,
        ManualAdjustmentSettings adjustment)
    {
        source.Validate();
        adjustment.Validate();
        MeshCalibration result = Clone(source);
        RectangleF sourceBounds = MeshWarp.GetInverseMappedBounds(source);
        double centerX = sourceBounds.Left + sourceBounds.Width / 2.0;
        double centerY = sourceBounds.Top + sourceBounds.Height / 2.0;
        double halfWidth = Math.Max(1, sourceBounds.Width / 2.0);
        double halfHeight = Math.Max(1, sourceBounds.Height / 2.0);
        double horizontalPerspective = adjustment.HorizontalPerspectivePercent / 100.0;
        double verticalPerspective = adjustment.VerticalPerspectivePercent / 100.0;
        double radians = adjustment.RotationDegrees * Math.PI / 180.0;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);

        result.ProjectorPoints = source.ProjectorPoints.Select(point =>
        {
            double dx = point.X - centerX;
            double dy = point.Y - centerY;
            double normalizedX = Math.Clamp(dx / halfWidth, -1.5, 1.5);
            double normalizedY = Math.Clamp(dy / halfHeight, -1.5, 1.5);

            // 正水平透视：左边纵向放大、右边纵向缩小；负值相反。
            double adjustedX = dx * (1 - verticalPerspective * normalizedY);
            double adjustedY = dy * (1 - horizontalPerspective * normalizedX);
            double rotatedX = cosine * adjustedX - sine * adjustedY;
            double rotatedY = sine * adjustedX + cosine * adjustedY;
            return new PointF(
                (float)(centerX + rotatedX + adjustment.OffsetX),
                (float)(centerY + rotatedY + adjustment.OffsetY));
        }).ToArray();
        result.CreatedAtUtc = DateTime.UtcNow;
        result.ManualAdjustment = Copy(adjustment);

        // 旋转和透视会改变轴对齐外接框，尤其是宽画面旋转几度时，高度会变化几十像素。
        // 用户填写的宽/高百分比定义的是最终实际像素外接尺寸；因此先完成形状调整，
        // 再独立归一化 X/Y。只有宽高缩放控件可以改变目标外接宽高。
        if (!adjustment.IsIdentity)
        {
            double targetWidth = source.ExpectedInverseMappedWidth *
                                 adjustment.HorizontalScalePercent / 100.0;
            double targetHeight = source.ExpectedInverseMappedHeight *
                                  adjustment.VerticalScalePercent / 100.0;
            NormalizeBounds(result, targetWidth, targetHeight, "人眼调整");
        }

        MeshGeometryDiagnostics geometry = AnalyzeAndValidate(result, "人眼调整");
        result.ExpectedInverseMappedWidth = Math.Clamp(
            (int)Math.Round(source.ExpectedInverseMappedWidth * adjustment.HorizontalScalePercent / 100.0),
            32, result.CanvasWidth);
        result.ExpectedInverseMappedHeight = Math.Clamp(
            (int)Math.Round(source.ExpectedInverseMappedHeight * adjustment.VerticalScalePercent / 100.0),
            32, result.CanvasHeight);
        result.Validate();
        return new ManualAdjustmentResult(result, geometry);
    }

    /// <summary>保持现有畸变形状，仅把最终轴对齐外接框缩放到指定像素宽高。</summary>
    public static ManualAdjustmentResult ResizeBounds(
        MeshCalibration source,
        int targetWidth,
        int targetHeight)
    {
        source.Validate();
        MeshCalibration result = Clone(source);
        NormalizeBounds(result, targetWidth, targetHeight, "目标尺寸调整");
        result.CreatedAtUtc = DateTime.UtcNow;
        result.ExpectedInverseMappedWidth = targetWidth;
        result.ExpectedInverseMappedHeight = targetHeight;
        MeshGeometryDiagnostics geometry = AnalyzeAndValidate(result, "目标尺寸调整");
        result.Validate();
        return new ManualAdjustmentResult(result, geometry);
    }

    private static void NormalizeBounds(
        MeshCalibration calibration,
        double targetWidth,
        double targetHeight,
        string operation)
    {
        if (targetWidth < 32 || targetWidth > calibration.CanvasWidth ||
            targetHeight < 32 || targetHeight > calibration.CanvasHeight)
        {
            throw new InvalidDataException(
                $"{operation}后的目标外接尺寸 {targetWidth:F1}×{targetHeight:F1} 超出 " +
                $"{calibration.CanvasWidth}×{calibration.CanvasHeight} 画布。");
        }

        RectangleF transformedBounds = MeshWarp.GetInverseMappedBounds(calibration);
        if (transformedBounds.Width < 1 || transformedBounds.Height < 1)
            throw new InvalidDataException($"{operation}后的网格外接尺寸无效。");
        double transformedCenterX = transformedBounds.Left + transformedBounds.Width / 2.0;
        double transformedCenterY = transformedBounds.Top + transformedBounds.Height / 2.0;
        double normalizeX = targetWidth / transformedBounds.Width;
        double normalizeY = targetHeight / transformedBounds.Height;
        calibration.ProjectorPoints = calibration.ProjectorPoints.Select(point => new PointF(
            (float)(transformedCenterX + (point.X - transformedCenterX) * normalizeX),
            (float)(transformedCenterY + (point.Y - transformedCenterY) * normalizeY))).ToArray();
    }

    private static MeshGeometryDiagnostics AnalyzeAndValidate(
        MeshCalibration calibration,
        string operation)
    {
        MeshGeometryDiagnostics geometry = MeshWarp.AnalyzeGeometry(calibration);
        var failures = new List<string>();
        if (geometry.OutsideCanvasPointCount > 0)
            failures.Add($"{geometry.OutsideCanvasPointCount} 个扩展网格点越出 1920×1080");
        if (geometry.FoldedTriangleCount > 0 || geometry.DegenerateTriangleCount > 0)
            failures.Add($"网格含 {geometry.FoldedTriangleCount} 个翻折、{geometry.DegenerateTriangleCount} 个退化三角形");
        if (geometry.MinimumControlPointSpacing < 3)
            failures.Add($"最小控制点间距只有 {geometry.MinimumControlPointSpacing:F1}px");
        if (failures.Count > 0)
            throw new InvalidDataException(operation + "安全检查失败：" +
                                           string.Join("；", failures) + "。请减小调节量。");
        return geometry;
    }

    private static MeshCalibration Clone(MeshCalibration source) => new()
    {
        Version = source.Version,
        CreatedAtUtc = source.CreatedAtUtc,
        CameraSerial = source.CameraSerial,
        CanvasWidth = source.CanvasWidth,
        CanvasHeight = source.CanvasHeight,
        SourceContentWidth = source.SourceContentWidth,
        SourceContentHeight = source.SourceContentHeight,
        ExpectedInverseMappedWidth = source.ExpectedInverseMappedWidth,
        ExpectedInverseMappedHeight = source.ExpectedInverseMappedHeight,
        Columns = source.Columns,
        Rows = source.Rows,
        SourcePoints = source.SourcePoints.ToArray(),
        ProjectorPoints = source.ProjectorPoints.ToArray(),
        DesiredCameraPoints = source.DesiredCameraPoints.ToArray(),
        LastObservedCameraPoints = source.LastObservedCameraPoints.ToArray(),
        LastRmsErrorPixels = source.LastRmsErrorPixels,
        DenseMapFile = source.DenseMapFile,
        ManualAdjustment = source.ManualAdjustment is null ? null : Copy(source.ManualAdjustment)
    };

    private static ManualAdjustmentSettings Copy(ManualAdjustmentSettings source) => new()
    {
        RotationDegrees = source.RotationDegrees,
        HorizontalPerspectivePercent = source.HorizontalPerspectivePercent,
        VerticalPerspectivePercent = source.VerticalPerspectivePercent,
        HorizontalScalePercent = source.HorizontalScalePercent,
        VerticalScalePercent = source.VerticalScalePercent,
        OffsetX = source.OffsetX,
        OffsetY = source.OffsetY
    };
}
