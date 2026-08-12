namespace AprilTagPose.Models;

public enum MarkerDictionaryKind
{
    ArUco4X4_250 = 0,
    AprilTag16h5 = 1
}

public static class MarkerDictionaryKindExtensions
{
    public static string DisplayName(this MarkerDictionaryKind dictionary) => dictionary switch
    {
        MarkerDictionaryKind.ArUco4X4_250 => "ArUco DICT_4X4_250",
        MarkerDictionaryKind.AprilTag16h5 => "AprilTag tag16h5",
        _ => throw new ArgumentOutOfRangeException(nameof(dictionary), dictionary, "不支持的标记字典。")
    };

    public static string ShortName(this MarkerDictionaryKind dictionary) => dictionary switch
    {
        MarkerDictionaryKind.ArUco4X4_250 => "DICT_4X4_250",
        MarkerDictionaryKind.AprilTag16h5 => "tag16h5",
        _ => throw new ArgumentOutOfRangeException(nameof(dictionary), dictionary, "不支持的标记字典。")
    };

    public static string CommandLineName(this MarkerDictionaryKind dictionary) => dictionary switch
    {
        MarkerDictionaryKind.ArUco4X4_250 => "4x4_250",
        MarkerDictionaryKind.AprilTag16h5 => "tag16h5",
        _ => throw new ArgumentOutOfRangeException(nameof(dictionary), dictionary, "不支持的标记字典。")
    };
}
