using System.Text.Json;
using System.Text.Json.Serialization;
using AprilTagPose.Models;

namespace AprilTagPose.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsStore(string? path = null)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AprilTagPose");
        SettingsPath = path ?? Path.Combine(directory, "settings.json");
    }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }

    public static CameraCalibration LoadCalibration(string path)
    {
        string json = File.ReadAllText(path);
        CameraCalibration? direct = JsonSerializer.Deserialize<CameraCalibration>(json, JsonOptions);
        if (direct?.IsValid == true) return direct;

        AppSettings? wrapped = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        if (wrapped?.Calibration.IsValid == true) return wrapped.Calibration;

        throw new InvalidDataException("标定文件中没有有效的 fx、fy、cx、cy。支持 CameraCalibration 或 AppSettings JSON。");
    }

    public static void SaveCalibration(string path, CameraCalibration calibration)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(calibration, JsonOptions));
    }
}
