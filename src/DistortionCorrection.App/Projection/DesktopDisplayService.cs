using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DistortionCorrection.Projection;

/// <summary>沿用参考工程的 Windows 桌面壁纸与 Win+P 拓扑切换方式。</summary>
public sealed class DesktopDisplayService : IDesktopDisplayService
{
    private const uint SpiGetDesktopWallpaper = 0x0073;
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendWinIniChange = 0x0002;
    private const uint SdcApply = 0x00000080;
    private const uint SdcTopologyInternal = 0x00000001;
    private const uint SdcTopologyClone = 0x00000002;
    private const uint SdcTopologyExtend = 0x00000004;
    private const uint SdcTopologyExternal = 0x00000008;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint pathCount, IntPtr paths, uint modeCount, IntPtr modes, uint flags);

    [ComImport]
    [Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperComObject;

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper(
            [MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        void GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect displayRect);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(DesktopWallpaperPosition position);
        DesktopWallpaperPosition GetPosition();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    public string? GetCurrentWallpaper()
    {
        var path = new StringBuilder(1024);
        return SystemParametersInfo(SpiGetDesktopWallpaper, (uint)path.Capacity, path, 0)
            ? path.ToString()
            : null;
    }

    public WallpaperSnapshot CaptureSnapshot()
    {
        var monitors = new List<MonitorWallpaper>();
        IDesktopWallpaper? desktop = null;
        try
        {
            desktop = (IDesktopWallpaper)new DesktopWallpaperComObject();
            uint count = desktop.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = desktop.GetMonitorDevicePathAt(i);
                string wallpaper = desktop.GetWallpaper(id);
                monitors.Add(new MonitorWallpaper(id, wallpaper));
            }
        }
        catch (Exception exception) when (IsDesktopWallpaperUnavailable(exception))
        {
            // 较老系统、Explorer 尚未就绪或当前会话不允许 COM 激活时，
            // 仍可使用下面的传统接口。
        }
        finally
        {
            if (desktop is not null) Marshal.FinalReleaseComObject(desktop);
        }
        return new WallpaperSnapshot(monitors, GetCurrentWallpaper());
    }

    public void RestoreSnapshot(WallpaperSnapshot snapshot)
    {
        if (snapshot.Monitors.Count > 0)
        {
            IDesktopWallpaper? desktop = null;
            try
            {
                desktop = (IDesktopWallpaper)new DesktopWallpaperComObject();
                foreach (MonitorWallpaper monitor in snapshot.Monitors)
                {
                    if (File.Exists(monitor.WallpaperPath)) desktop.SetWallpaper(monitor.MonitorId, monitor.WallpaperPath);
                }
                return;
            }
            finally
            {
                if (desktop is not null) Marshal.FinalReleaseComObject(desktop);
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LegacyWallpaper) && File.Exists(snapshot.LegacyWallpaper))
        {
            SetWallpaper(snapshot.LegacyWallpaper);
        }
    }

    public void ApplyTopology(DisplayTopology topology)
    {
        if (topology == DisplayTopology.None) return;
        uint flag = topology switch
        {
            DisplayTopology.Internal => SdcTopologyInternal,
            DisplayTopology.Clone => SdcTopologyClone,
            DisplayTopology.External => SdcTopologyExternal,
            DisplayTopology.Extend => SdcTopologyExtend,
            _ => throw new ArgumentOutOfRangeException(nameof(topology))
        };
        int result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SdcApply | flag);
        if (result != 0) throw new Win32Exception(result, "切换 Windows 投影模式失败。");
    }

    public void SetWallpaper(string imagePath)
    {
        string fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("投图文件不存在。", fullPath);
        string bmpPath = ConvertToBmpIfNeeded(fullPath);
        bool legacySuccess = SystemParametersInfo(
            SpiSetDesktopWallpaper, 0, bmpPath, SpifUpdateIniFile | SpifSendWinIniChange);

        // Windows 10/11 扩展桌面允许每个显示器使用独立壁纸；传统 API 只更新一个逻辑壁纸。
        // monitorId=null 会把同一图卡同步应用到所有显示器，正好满足循环标定切图要求。
        IDesktopWallpaper? desktop = null;
        try
        {
            desktop = (IDesktopWallpaper)new DesktopWallpaperComObject();
            desktop.SetPosition(DesktopWallpaperPosition.Fit);
            desktop.SetWallpaper(null, bmpPath);
        }
        catch (Exception exception) when (legacySuccess && IsDesktopWallpaperUnavailable(exception))
        {
            return;
        }
        finally
        {
            if (desktop is not null) Marshal.FinalReleaseComObject(desktop);
        }

        if (!legacySuccess)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "切换桌面图片失败。");
        }
    }

    private static bool IsDesktopWallpaperUnavailable(Exception exception) =>
        exception is COMException or UnauthorizedAccessException;

    internal static string ConvertToBmpIfNeeded(string imagePath)
    {
        if (string.Equals(Path.GetExtension(imagePath), ".bmp", StringComparison.OrdinalIgnoreCase)) return imagePath;
        var info = new FileInfo(imagePath);
        string key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20];
        string directory = Path.Combine(Path.GetTempPath(), "DistortionCorrection", "WallpaperCache");
        Directory.CreateDirectory(directory);
        string result = Path.Combine(directory, $"wallpaper-{hash}.bmp");
        if (File.Exists(result)) return result;

        string temporary = result + ".tmp";
        try
        {
            using Image source = Image.FromFile(imagePath);
            using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            bitmap.Save(temporary, ImageFormat.Bmp);
            File.Move(temporary, result, true);
            return result;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
