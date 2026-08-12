using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DistortionCorrection.Models;

public sealed class AppSettings
{
    public const int RequiredCanvasWidth = 1920;
    public const int RequiredCanvasHeight = 1080;
    public const int RequiredSourceContentWidth = 1920;
    public const int RequiredSourceContentHeight = 1080;
    public const int ExpectedWarpedWidth = 1777;
    public const int ExpectedWarpedHeight = 687;
    public const int MinimumGridSize = 3;
    public const int MaximumGridPointCount = 5000;

    public int CanvasWidth { get; set; } = RequiredCanvasWidth;
    public int CanvasHeight { get; set; } = RequiredCanvasHeight;
    public int SourceContentWidth { get; set; } = RequiredSourceContentWidth;
    public int SourceContentHeight { get; set; } = RequiredSourceContentHeight;
    public int ExpectedInverseMappedWidth { get; set; } = ExpectedWarpedWidth;
    public int ExpectedInverseMappedHeight { get; set; } = ExpectedWarpedHeight;
    public int GridColumns { get; set; } = 27;
    public int GridRows { get; set; } = 7;
    public int DotDiameter { get; set; } = 13;
    public int GrayCodeCellSize { get; set; } = 8;
    public int ProjectionSettleMilliseconds { get; set; } = 900;
    public int CaptureTimeoutMilliseconds { get; set; } = 3000;
    public int MinimumGrayCodeContrast { get; set; } = 12;
    public int MaximumLoopIterations { get; set; } = 8;
    public double LoopRelaxation { get; set; } = 0.75;
    public double TargetRmsPixels { get; set; } = 1.5;
    public string PreferredCameraSerial { get; set; } = "DA2239447";
    public string PreferredCameraModel { get; set; } = "MV-CS200-10GM";
    public string CameraIp { get; set; } = "169.254.18.15";
    public string HostAdapterIp { get; set; } = "169.254.163.144";
    public double? ExposureTimeMicroseconds { get; set; }
    public double? Gain { get; set; }
    public bool LensCorrectionEnabled { get; set; }
    public string LensCalibrationPath { get; set; } = string.Empty;
    public string LensChessboardImageDirectory { get; set; } = string.Empty;
    public double LensCorrectionAlpha { get; set; }
    public int LensChessboardColumns { get; set; } = 9;
    public int LensChessboardRows { get; set; } = 6;
    public double LensChessboardSquareSize { get; set; } = 25.0;
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "DistortionCorrection", "Runs");

    [JsonIgnore]
    public Rectangle SourceContentRectangle => new(
        (CanvasWidth - SourceContentWidth) / 2,
        (CanvasHeight - SourceContentHeight) / 2,
        SourceContentWidth,
        SourceContentHeight);

    [JsonIgnore]
    public Rectangle CanvasRectangle => new(0, 0, CanvasWidth, CanvasHeight);

    public void Validate()
    {
        if (CanvasWidth != RequiredCanvasWidth || CanvasHeight != RequiredCanvasHeight)
        {
            throw new InvalidOperationException("输出画布必须固定为 1920×1080。");
        }

        if (SourceContentWidth != RequiredSourceContentWidth || SourceContentHeight != RequiredSourceContentHeight)
        {
            throw new InvalidOperationException("任意输入按完整 1920×1080 画布进行逆映射。");
        }

        if (ExpectedInverseMappedWidth < 32 || ExpectedInverseMappedWidth > CanvasWidth ||
            ExpectedInverseMappedHeight < 32 || ExpectedInverseMappedHeight > CanvasHeight)
        {
            throw new InvalidOperationException(
                $"逆映射目标外接尺寸必须在 32×32 到 {CanvasWidth}×{CanvasHeight} 之间。");
        }

        if (GridColumns < MinimumGridSize || GridRows < MinimumGridSize)
            throw new InvalidOperationException($"点阵行列数均不能小于 {MinimumGridSize}。");
        if (checked(GridColumns * GridRows) > MaximumGridPointCount)
            throw new InvalidOperationException($"点阵总点数不能超过 {MaximumGridPointCount}。");
        if (DotDiameter < 3 || DotDiameter > 199) throw new InvalidOperationException("点直径必须在 3 到 199 像素之间。");
        if (GrayCodeCellSize < 1 || GrayCodeCellSize > 64) throw new InvalidOperationException("Gray Code 单元尺寸无效。");
        if (ProjectionSettleMilliseconds < 100) throw new InvalidOperationException("切图稳定时间不能小于 100 ms。");
        if (LensCorrectionAlpha is < 0 or > 1)
            throw new InvalidOperationException("镜头矫正 Alpha 必须在 0 到 1 之间。");
        if (LensChessboardColumns < 3 || LensChessboardRows < 3)
            throw new InvalidOperationException("棋盘格内角点列数和行数均不能小于 3。");
        if (LensChessboardSquareSize <= 0)
            throw new InvalidOperationException("棋盘格方格边长必须大于 0。");
        if (LensCorrectionEnabled && string.IsNullOrWhiteSpace(LensCalibrationPath))
            throw new InvalidOperationException("启用镜头畸变矫正时必须选择镜头标定 JSON。");
    }

    public static string DefaultSettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultSettingsPath;
        if (!File.Exists(path)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions())
            ?? new AppSettings();
        settings.Validate();
        return settings;
    }

    public void Save(string? path = null)
    {
        Validate();
        path ??= DefaultSettingsPath;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
