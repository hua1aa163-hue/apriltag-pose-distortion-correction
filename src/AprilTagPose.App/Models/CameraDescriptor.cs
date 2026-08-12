using MvCameraControl;

namespace AprilTagPose.Models;

/// <summary>
/// 相机的传输层大类。
/// </summary>
public enum CameraTransportType
{
    Unknown,
    GigE,
    Usb,
    GenTL
}

/// <summary>
/// 海康 SDK 枚举得到的相机描述。实例由 <c>HikCameraService</c> 创建。
/// </summary>
public sealed class CameraDescriptor
{
    internal CameraDescriptor(IDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        NativeDeviceInfo = deviceInfo;
        Transport = GetTransport(deviceInfo.TLayerType);
        TransportDetail = GetTransportDetail(deviceInfo.TLayerType);
        Manufacturer = deviceInfo.ManufacturerName?.Trim() ?? string.Empty;
        Model = deviceInfo.ModelName?.Trim() ?? string.Empty;
        SerialNumber = deviceInfo.SerialNumber?.Trim() ?? string.Empty;
        UserDefinedName = deviceInfo.UserDefinedName?.Trim() ?? string.Empty;
        DeviceVersion = deviceInfo.DeviceVersion?.Trim() ?? string.Empty;

        string baseName = !string.IsNullOrWhiteSpace(UserDefinedName)
            ? UserDefinedName
            : string.Join(' ', new[] { Manufacturer, Model }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "未命名相机";
        }

        DisplayName = string.IsNullOrWhiteSpace(SerialNumber)
            ? $"{TransportDetail}: {baseName}"
            : $"{TransportDetail}: {baseName} ({SerialNumber})";

        Id = $"{(int)deviceInfo.TLayerType}:{SerialNumber}:{Manufacturer}:{Model}";
    }

    /// <summary>在本次枚举结果中稳定的设备标识。</summary>
    public string Id { get; }

    /// <summary>适合直接显示在相机下拉框中的名称。</summary>
    public string DisplayName { get; }

    public CameraTransportType Transport { get; }

    /// <summary>具体传输层，例如 GigE、USB3、GenTL-CXP。</summary>
    public string TransportDetail { get; }

    public string Manufacturer { get; }

    public string Model { get; }

    public string SerialNumber { get; }

    public string UserDefinedName { get; }

    public string DeviceVersion { get; }

    internal IDeviceInfo NativeDeviceInfo { get; }

    public override string ToString() => DisplayName;

    private static CameraTransportType GetTransport(DeviceTLayerType layerType) => layerType switch
    {
        DeviceTLayerType.MvGigEDevice => CameraTransportType.GigE,
        DeviceTLayerType.MvUsbDevice => CameraTransportType.Usb,
        DeviceTLayerType.MvGenTLGigEDevice or
        DeviceTLayerType.MvGenTLCameraLinkDevice or
        DeviceTLayerType.MvGenTLCXPDevice or
        DeviceTLayerType.MvGenTLXoFDevice => CameraTransportType.GenTL,
        _ => CameraTransportType.Unknown
    };

    private static string GetTransportDetail(DeviceTLayerType layerType) => layerType switch
    {
        DeviceTLayerType.MvGigEDevice => "GigE",
        DeviceTLayerType.MvUsbDevice => "USB3",
        DeviceTLayerType.MvGenTLGigEDevice => "GenTL-GigE",
        DeviceTLayerType.MvGenTLCameraLinkDevice => "GenTL-CameraLink",
        DeviceTLayerType.MvGenTLCXPDevice => "GenTL-CXP",
        DeviceTLayerType.MvGenTLXoFDevice => "GenTL-XoFLink",
        _ => layerType.ToString()
    };
}
