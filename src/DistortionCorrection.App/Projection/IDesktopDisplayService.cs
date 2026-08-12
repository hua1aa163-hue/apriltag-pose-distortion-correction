namespace DistortionCorrection.Projection;

public interface IDesktopDisplayService
{
    string? GetCurrentWallpaper();
    WallpaperSnapshot CaptureSnapshot();
    void RestoreSnapshot(WallpaperSnapshot snapshot);
    void ApplyTopology(DisplayTopology topology);
    void SetWallpaper(string imagePath);
}

public sealed record MonitorWallpaper(string MonitorId, string WallpaperPath);
public sealed record WallpaperSnapshot(IReadOnlyList<MonitorWallpaper> Monitors, string? LegacyWallpaper);
