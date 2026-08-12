namespace AprilTagPose.Models;

public sealed class AppSettings
{
    /// <summary>方形视觉标记外侧黑色正方形的实际边长，单位 mm。</summary>
    public double TagSizeMillimeters { get; set; } = 50d;
    public MarkerDictionaryKind MarkerDictionary { get; set; } = MarkerDictionaryKind.ArUco4X4_250;
    public CameraCalibration Calibration { get; set; } = new();
    public string LastImagePath { get; set; } = @"C:\Users\admin\MVS\Data\Image_20260810145722324.bmp";
    public string LastExportDirectory { get; set; } = string.Empty;
    public int LiveProcessingIntervalMilliseconds { get; set; } = 100;
    public bool AutoLoadLastImage { get; set; } = true;
}
