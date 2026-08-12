using System.Text.Json.Serialization;

namespace AprilTagPose.Models;

/// <summary>
/// OpenCV 针孔相机模型。畸变系数顺序为 k1, k2, p1, p2, k3。
/// </summary>
public sealed class CameraCalibration
{
    public double Fx { get; set; }
    public double Fy { get; set; }
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double K1 { get; set; }
    public double K2 { get; set; }
    public double P1 { get; set; }
    public double P2 { get; set; }
    public double K3 { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string CameraSerialNumber { get; set; } = string.Empty;
    public DateTimeOffset? CalibratedAt { get; set; }

    [JsonIgnore]
    public bool IsValid =>
        double.IsFinite(Fx) && double.IsFinite(Fy) && Fx > 0 && Fy > 0 &&
        double.IsFinite(Cx) && double.IsFinite(Cy);

    [JsonIgnore]
    public double[,] CameraMatrix => new[,]
    {
        { Fx, 0d, Cx },
        { 0d, Fy, Cy },
        { 0d, 0d, 1d }
    };

    [JsonIgnore]
    public double[] DistortionCoefficients => [K1, K2, P1, P2, K3];

    public CameraCalibration Clone() => new()
    {
        Fx = Fx,
        Fy = Fy,
        Cx = Cx,
        Cy = Cy,
        K1 = K1,
        K2 = K2,
        P1 = P1,
        P2 = P2,
        K3 = K3,
        ImageWidth = ImageWidth,
        ImageHeight = ImageHeight,
        CameraSerialNumber = CameraSerialNumber,
        CalibratedAt = CalibratedAt
    };

    /// <summary>
    /// 仅供没有标定文件时预览使用。焦距取图像长边像素数，不能替代真实标定。
    /// </summary>
    public static CameraCalibration CreateApproximate(int width, int height) => new()
    {
        Fx = Math.Max(width, height),
        Fy = Math.Max(width, height),
        Cx = (width - 1) / 2d,
        Cy = (height - 1) / 2d,
        ImageWidth = width,
        ImageHeight = height
    };

    public CameraCalibration ForImageSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "当前图像分辨率无效。");

        if (IsValid && ImageWidth > 0 && ImageHeight > 0 &&
            (ImageWidth != width || ImageHeight != height))
        {
            throw new InvalidOperationException(
                $"标定分辨率为 {ImageWidth}×{ImageHeight}，当前图像为 {width}×{height}。" +
                "工业相机改变 Width/Height 通常表示 ROI 裁剪，不能按比例自动缩放内参；" +
                "请加载当前 ROI/分辨率的标定结果或手工填写换算后的内参。");
        }

        CameraCalibration copy = Clone();
        copy.ImageWidth = width;
        copy.ImageHeight = height;
        return copy;
    }
}
