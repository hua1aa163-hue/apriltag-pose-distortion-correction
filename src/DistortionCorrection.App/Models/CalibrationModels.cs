using System.Drawing;
using System.Text.Json.Serialization;

namespace DistortionCorrection.Models;

public sealed class MeshCalibration
{
    public int Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CameraSerial { get; set; } = string.Empty;
    public int CanvasWidth { get; set; } = AppSettings.RequiredCanvasWidth;
    public int CanvasHeight { get; set; } = AppSettings.RequiredCanvasHeight;
    public int SourceContentWidth { get; set; } = AppSettings.RequiredSourceContentWidth;
    public int SourceContentHeight { get; set; } = AppSettings.RequiredSourceContentHeight;
    public int ExpectedInverseMappedWidth { get; set; } = AppSettings.ExpectedWarpedWidth;
    public int ExpectedInverseMappedHeight { get; set; } = AppSettings.ExpectedWarpedHeight;
    public int Columns { get; set; } = 27;
    public int Rows { get; set; } = 7;
    public PointF[] SourcePoints { get; set; } = [];
    public PointF[] ProjectorPoints { get; set; } = [];
    public PointF[] DesiredCameraPoints { get; set; } = [];
    public PointF[] LastObservedCameraPoints { get; set; } = [];
    /// <summary>-1 表示尚未执行点阵回环；JSON 中避免使用无法直接序列化的 NaN。</summary>
    public double LastRmsErrorPixels { get; set; } = -1;
    public string? DenseMapFile { get; set; }
    public ManualAdjustmentSettings? ManualAdjustment { get; set; }

    [JsonIgnore]
    public Rectangle SourceContentRectangle => new(
        (CanvasWidth - SourceContentWidth) / 2,
        (CanvasHeight - SourceContentHeight) / 2,
        SourceContentWidth,
        SourceContentHeight);

    public void Validate()
    {
        int expected = checked(Columns * Rows);
        if (CanvasWidth != 1920 || CanvasHeight != 1080 ||
            SourceContentWidth != 1920 || SourceContentHeight != 1080)
        {
            throw new InvalidDataException("标定的输入和输出画布必须均为 1920×1080。");
        }
        if (ExpectedInverseMappedWidth < 32 || ExpectedInverseMappedWidth > CanvasWidth ||
            ExpectedInverseMappedHeight < 32 || ExpectedInverseMappedHeight > CanvasHeight)
        {
            throw new InvalidDataException(
                $"标定的逆映射目标尺寸必须在 32×32 到 {CanvasWidth}×{CanvasHeight} 之间。");
        }
        if (Columns < AppSettings.MinimumGridSize || Rows < AppSettings.MinimumGridSize ||
            expected > AppSettings.MaximumGridPointCount)
        {
            throw new InvalidDataException("标定点阵行列数或总点数无效。");
        }

        if (SourcePoints.Length != expected || ProjectorPoints.Length != expected)
        {
            throw new InvalidDataException($"标定网格点数应为 {expected}。");
        }
        if (DesiredCameraPoints.Length is not 0 && DesiredCameraPoints.Length != expected)
            throw new InvalidDataException($"目标相机点数应为 0 或 {expected}。");
        if (LastObservedCameraPoints.Length is not 0 && LastObservedCameraPoints.Length != expected)
            throw new InvalidDataException($"最近观测点数应为 0 或 {expected}。");
        if (SourcePoints.Concat(ProjectorPoints).Any(point =>
                !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
        {
            throw new InvalidDataException("标定网格包含非有限坐标。");
        }
    }
}

public sealed class ManualAdjustmentSettings
{
    /// <summary>正数为顺时针，负数为逆时针。</summary>
    public double RotationDegrees { get; set; }
    /// <summary>正数为左大右小，负数为左小右大。</summary>
    public double HorizontalPerspectivePercent { get; set; }
    /// <summary>正数为上大下小，负数为上小下大。</summary>
    public double VerticalPerspectivePercent { get; set; }
    public double HorizontalScalePercent { get; set; } = 100;
    public double VerticalScalePercent { get; set; } = 100;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }

    [JsonIgnore]
    public bool IsIdentity =>
        Math.Abs(RotationDegrees) < 1e-9 &&
        Math.Abs(HorizontalPerspectivePercent) < 1e-9 &&
        Math.Abs(VerticalPerspectivePercent) < 1e-9 &&
        Math.Abs(HorizontalScalePercent - 100) < 1e-9 &&
        Math.Abs(VerticalScalePercent - 100) < 1e-9 &&
        Math.Abs(OffsetX) < 1e-9 &&
        Math.Abs(OffsetY) < 1e-9;

    public void Validate()
    {
        if (RotationDegrees is < -45 or > 45) throw new InvalidDataException("旋转角度必须在 -45° 到 45° 之间。");
        if (HorizontalPerspectivePercent is < -50 or > 50 || VerticalPerspectivePercent is < -50 or > 50)
            throw new InvalidDataException("透视调节必须在 -50% 到 50% 之间。");
        if (HorizontalScalePercent is < 25 or > 200 || VerticalScalePercent is < 25 or > 200)
            throw new InvalidDataException("宽高缩放必须在 25% 到 200% 之间。");
        if (OffsetX is < -1920 or > 1920 || OffsetY is < -1080 or > 1080)
            throw new InvalidDataException("平移量超出允许范围。");
    }
}

public sealed class DenseProjectorMap
{
    public const uint FileMagic = 0x504D4344; // DCMP
    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }
    public int CellSize { get; init; }
    public int CellColumns { get; init; }
    public int CellRows { get; init; }
    public int CameraWidth { get; init; }
    public int CameraHeight { get; init; }
    public PointF[] CameraAtProjectorCell { get; init; } = [];
    public int[] SamplesPerCell { get; init; } = [];

    public bool IsValid(int column, int row)
    {
        int i = checked(row * CellColumns + column);
        return (uint)i < (uint)SamplesPerCell.Length && SamplesPerCell[i] > 0 &&
               float.IsFinite(CameraAtProjectorCell[i].X) && float.IsFinite(CameraAtProjectorCell[i].Y);
    }
}
