using System.Text.Json;

namespace DistortionCorrection.Camera;

/// <summary>OpenCV 针孔相机模型的内参与镜头畸变参数。</summary>
public sealed class LensCalibration
{
    public int FormatVersion { get; set; } = 1;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    /// <summary>按行展开的 3×3 相机内参矩阵。</summary>
    public double[] CameraMatrix { get; set; } = [];

    /// <summary>OpenCV 顺序：k1, k2, p1, p2, k3（可扩展至 8/12/14 项）。</summary>
    public double[] DistortionCoefficients { get; set; } = [];
    public double RmsReprojectionError { get; set; }
    public double MeanReprojectionError { get; set; }
    public int UsedImageCount { get; set; }
    public int RejectedImageCount { get; set; }
    public int ChessboardColumns { get; set; }
    public int ChessboardRows { get; set; }
    public double ChessboardSquareSize { get; set; }
    public string CameraSerial { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string[] AcceptedImages { get; set; } = [];
    public string[] RejectedImages { get; set; } = [];

    public void Validate()
    {
        if (FormatVersion != 1) throw new InvalidDataException($"不支持的镜头标定格式版本：{FormatVersion}。");
        if (ImageWidth <= 0 || ImageHeight <= 0) throw new InvalidDataException("镜头标定的图像尺寸无效。");
        if (CameraMatrix.Length != 9 || CameraMatrix.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("镜头标定的相机内参矩阵必须包含 9 个有效数值。");
        if (CameraMatrix[0] <= 0 || CameraMatrix[4] <= 0 || Math.Abs(CameraMatrix[8]) < 1e-12)
            throw new InvalidDataException("镜头标定的焦距或齐次尺度无效。");
        if (DistortionCoefficients.Length is not (4 or 5 or 8 or 12 or 14) ||
            DistortionCoefficients.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException("镜头畸变系数必须包含 4、5、8、12 或 14 个有效数值。");
        }
        if (!double.IsFinite(RmsReprojectionError) || RmsReprojectionError < 0)
            throw new InvalidDataException("镜头标定 RMS 无效。");
    }

    public double[,] ToCameraMatrix() => new[,]
    {
        { CameraMatrix[0], CameraMatrix[1], CameraMatrix[2] },
        { CameraMatrix[3], CameraMatrix[4], CameraMatrix[5] },
        { CameraMatrix[6], CameraMatrix[7], CameraMatrix[8] }
    };

    public void Save(string path)
    {
        Validate();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(this, JsonOptions()));
    }

    public static LensCalibration Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("镜头标定 JSON 不存在。", fullPath);
        LensCalibration calibration = JsonSerializer.Deserialize<LensCalibration>(
            File.ReadAllText(fullPath), JsonOptions())
            ?? throw new InvalidDataException("镜头标定 JSON 内容为空。");
        calibration.Validate();
        return calibration;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
