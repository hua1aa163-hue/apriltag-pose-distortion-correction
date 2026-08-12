using System.Globalization;
using AprilTagPose.Imaging;
using AprilTagPose.Models;
using OpenCvSharp;
using OpenCvSharp.Aruco;

namespace AprilTagPose.Services;

internal static class CommandLineRunner
{
    public static int Run(string[] args)
    {
        try
        {
            if (args[0].Equals("--analyze", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return Analyze(args);
            }

            return 64;
        }
        catch (Exception exception)
        {
            string? errorPath = SafeValueAfter(args, "--error");
            if (!string.IsNullOrWhiteSpace(errorPath))
            {
                try
                {
                    EnsureParentDirectory(errorPath);
                    File.WriteAllText(errorPath, exception.ToString());
                }
                catch
                {
                    // 保留原始退出码；错误报告路径本身无效时不覆盖原异常。
                }
            }
            return 1;
        }
    }

    private static int Analyze(string[] args)
    {
        bool selfTest = args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase);
        ValidateAnalyzeArguments(args, selfTest);
        string? positionalImage = FindPositionalImage(args);
        if (selfTest && string.IsNullOrWhiteSpace(positionalImage))
        {
            return RunSyntheticSelfTest(args);
        }

        if (string.IsNullOrWhiteSpace(positionalImage))
        {
            throw new ArgumentException("--analyze 后必须提供待检测图片路径。");
        }

        string imagePath = positionalImage;
        if (!File.Exists(imagePath)) throw new FileNotFoundException("找不到待检测图片。", imagePath);

        double tagSize = 50d;
        string? tagSizeText = ValueAfter(args, "--tag-size-mm");
        if (!string.IsNullOrWhiteSpace(tagSizeText) &&
            !double.TryParse(tagSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out tagSize))
        {
            throw new ArgumentException("--tag-size-mm 必须是有效数字。");
        }

        MarkerDictionaryKind dictionaryKind = ParseDictionary(ValueAfter(args, "--dictionary"));

        CameraCalibration calibration = new();
        string? calibrationPath = ValueAfter(args, "--calibration");
        if (!string.IsNullOrWhiteSpace(calibrationPath))
        {
            calibration = SettingsStore.LoadCalibration(calibrationPath);
        }

        using Mat image = Cv2.ImRead(imagePath, ImreadModes.Unchanged);
        if (image.Empty()) throw new InvalidDataException($"OpenCV 无法读取图片：{imagePath}");
        using var detector = new AprilTagPoseDetector(dictionaryKind);
        using DetectionFrameResult result = detector.Detect(image, calibration, tagSize, File.GetLastWriteTime(imagePath));

        string? overlayPath = ValueAfter(args, "--overlay");
        if (!string.IsNullOrWhiteSpace(overlayPath))
        {
            EnsureParentDirectory(overlayPath);
            if (!Cv2.ImWrite(overlayPath, result.AnnotatedImage))
                throw new IOException($"叠加图保存失败：{overlayPath}");
        }

        string? jsonPath = ValueAfter(args, "--json");
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureParentDirectory(jsonPath);
            DetectionExporter.SaveJson(jsonPath, result, imagePath, tagSize);
        }

        if (selfTest)
        {
            if (result.Tags.Count == 0) return 2;
            if (result.Tags.Any(static tag => tag.TranslationMillimeters.Length != 3)) return 3;
        }
        return 0;
    }

    private static int RunSyntheticSelfTest(string[] args)
    {
        const int markerId = 7;
        const double tagSizeMillimeters = 50d;
        MarkerDictionaryKind dictionaryKind = ParseDictionary(ValueAfter(args, "--dictionary"));
        using OpenCvSharp.Aruco.Dictionary dictionary =
            CvAruco.GetPredefinedDictionary(dictionaryKind switch
            {
                MarkerDictionaryKind.ArUco4X4_250 => PredefinedDictionaryType.Dict4X4_250,
                MarkerDictionaryKind.AprilTag16h5 => PredefinedDictionaryType.DictAprilTag_16h5,
                _ => throw new ArgumentOutOfRangeException(nameof(dictionaryKind))
            });
        using var marker = new Mat();
        dictionary.GenerateImageMarker(markerId, 300, marker, 1);
        using var canvas = new Mat();
        Cv2.CopyMakeBorder(marker, canvas, 200, 200, 300, 300, BorderTypes.Constant, Scalar.White);

        string? generatedInputPath = ValueAfter(args, "--generated-input");
        if (!string.IsNullOrWhiteSpace(generatedInputPath))
        {
            EnsureParentDirectory(generatedInputPath);
            if (!Cv2.ImWrite(generatedInputPath, canvas)) return 10;
        }

        var calibration = new CameraCalibration
        {
            Fx = 900,
            Fy = 900,
            Cx = 449.5,
            Cy = 349.5,
            ImageWidth = canvas.Width,
            ImageHeight = canvas.Height
        };
        using var detector = new AprilTagPoseDetector(dictionaryKind);
        using DetectionFrameResult result = detector.Detect(canvas, calibration, tagSizeMillimeters);
        using var canvas16 = new Mat();
        canvas.ConvertTo(canvas16, MatType.CV_16U, 256d);
        using DetectionFrameResult result16 = detector.Detect(canvas16, calibration, tagSizeMillimeters);
        using Bitmap preview16 = BitmapMatConverter.ToBitmap(canvas16);

        string? overlayPath = ValueAfter(args, "--overlay");
        if (!string.IsNullOrWhiteSpace(overlayPath))
        {
            EnsureParentDirectory(overlayPath);
            if (!Cv2.ImWrite(overlayPath, result.AnnotatedImage)) return 10;
        }

        string? jsonPath = ValueAfter(args, "--json");
        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            EnsureParentDirectory(jsonPath);
            DetectionExporter.SaveJson(
                jsonPath,
                result,
                $"generated:{dictionaryKind.CommandLineName()}-id7",
                tagSizeMillimeters);
        }

        if (result.Tags.Count != 1 || result.Tags[0].Id != markerId) return 2;
        if (result16.Tags.Count != 1 || result16.Tags[0].Id != markerId ||
            preview16.Width != canvas.Width || preview16.Height != canvas.Height) return 4;
        TagPoseResult tag = result.Tags[0];
        if (tag.TranslationMillimeters.Length != 3 ||
            tag.TranslationMillimeters.Any(static value => !double.IsFinite(value)) ||
            tag.TranslationMillimeters[2] <= 0 ||
            !double.IsFinite(tag.ReprojectionErrorPixels)) return 3;
        return 0;
    }

    private static string? ValueAfter(IReadOnlyList<string> args, string option)
    {
        for (int index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return null;
    }

    private static string? SafeValueAfter(IReadOnlyList<string> args, string option)
    {
        string? value = ValueAfter(args, option);
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal)
            ? null
            : value;
    }

    private static void ValidateAnalyzeArguments(IReadOnlyList<string> args, bool selfTest)
    {
        var optionsWithValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--tag-size-mm", "--dictionary", "--calibration", "--overlay", "--json", "--error", "--generated-input"
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int positionalCount = 0;
        for (int index = 1; index < args.Count; index++)
        {
            string token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionalCount++;
                continue;
            }

            if (!optionsWithValue.Contains(token))
                throw new ArgumentException($"未知选项：{token}");
            if (!seen.Add(token))
                throw new ArgumentException($"选项不能重复：{token}");
            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"选项 {token} 缺少值。");
            index++;
        }

        if (positionalCount > 1)
            throw new ArgumentException("只能提供一个待检测图片路径。");
        if (!selfTest && positionalCount != 1)
            throw new ArgumentException("--analyze 后必须提供待检测图片路径。");
        if (!selfTest && seen.Contains("--generated-input"))
            throw new ArgumentException("--generated-input 只能与 --self-test 一起使用。");
    }

    private static string? FindPositionalImage(IReadOnlyList<string> args)
    {
        var optionsWithValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--tag-size-mm", "--dictionary", "--calibration", "--overlay", "--json", "--error", "--generated-input"
        };
        for (int index = 1; index < args.Count; index++)
        {
            string value = args[index];
            if (optionsWithValue.Contains(value))
            {
                index++;
                continue;
            }

            if (!value.StartsWith("--", StringComparison.Ordinal)) return value;
        }
        return null;
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    private static MarkerDictionaryKind ParseDictionary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return AprilTagPoseDetector.DefaultDictionary;
        return value.Trim().ToLowerInvariant() switch
        {
            "4x4_250" or "dict_4x4_250" or "aruco4x4_250" => MarkerDictionaryKind.ArUco4X4_250,
            "tag16h5" or "apriltag16h5" or "dict_apriltag_16h5" => MarkerDictionaryKind.AprilTag16h5,
            _ => throw new ArgumentException(
                $"不支持的 --dictionary 值：{value}。可用值：4x4_250、tag16h5。")
        };
    }
}
