using DistortionCorrection.Models;
using MvCameraControl;

namespace DistortionCorrection.Camera;

/// <summary>海康 MVS .NET SDK 的最小、安全封装，只负责枚举、连接和单帧采集。</summary>
public sealed class HikCameraService : ICameraService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, IDeviceInfo> _deviceInfos = new(StringComparer.OrdinalIgnoreCase);
    private IDevice? _device;
    private readonly OpenCvLensCorrectionService _lensCorrection = new();
    private bool _sdkInitialized;
    private bool _grabbing;

    public HikCameraService()
    {
        SDKSystem.Initialize();
        _sdkInitialized = true;
    }

    public bool IsConnected => _device is not null;
    public CameraDescriptor? ConnectedCamera { get; private set; }

    public IReadOnlyList<CameraDescriptor> Enumerate()
    {
        lock (_sync)
        {
            const DeviceTLayerType layers = DeviceTLayerType.MvGigEDevice |
                                            DeviceTLayerType.MvGenTLGigEDevice;
            int result = DeviceEnumerator.EnumDevices(layers, out List<IDeviceInfo> devices);
            EnsureOk(result, "枚举相机");
            _deviceInfos.Clear();

            var descriptors = new List<CameraDescriptor>(devices.Count);
            foreach (IDeviceInfo info in devices)
            {
                string ip = string.Empty;
                string hostIp = string.Empty;
                if (info is IGigEDeviceInfo gigE)
                {
                    ip = FormatIp(gigE.CurrentIp);
                    hostIp = FormatIp(gigE.HostIP);
                }

                string id = string.IsNullOrWhiteSpace(info.SerialNumber)
                    ? $"{info.ModelName}|{ip}"
                    : info.SerialNumber;
                _deviceInfos[id] = info;
                descriptors.Add(new CameraDescriptor(
                    id, info.ModelName ?? string.Empty, info.SerialNumber ?? string.Empty, ip, hostIp));
            }

            return descriptors;
        }
    }

    public void Connect(CameraDescriptor descriptor, AppSettings settings)
    {
        lock (_sync)
        {
            DisconnectCore();
            LensCalibration? lensCalibration = null;
            if (settings.LensCorrectionEnabled)
            {
                lensCalibration = LensCalibration.Load(settings.LensCalibrationPath);
                if (!string.IsNullOrWhiteSpace(lensCalibration.CameraSerial) &&
                    !string.Equals(lensCalibration.CameraSerial, descriptor.SerialNumber,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"镜头标定属于相机 {lensCalibration.CameraSerial}，当前选择的是 " +
                        $"{descriptor.SerialNumber}。请为当前相机重新标定镜头。");
                }
            }
            _lensCorrection.Configure(lensCalibration, settings.LensCorrectionAlpha);
            if (!_deviceInfos.TryGetValue(descriptor.Id, out IDeviceInfo? info))
            {
                Enumerate();
                if (!_deviceInfos.TryGetValue(descriptor.Id, out info))
                {
                    throw new InvalidOperationException($"相机已不在线：{descriptor}");
                }
            }

            IDevice? device = DeviceFactory.CreateDevice(info);
            if (device is null) throw new InvalidOperationException("MVS SDK 创建设备失败。");

            try
            {
                EnsureOk(device.Open(), "打开相机");
                if (device is IGigEDevice gigE)
                {
                    int packetResult = gigE.GetOptimalPacketSize(out int packetSize);
                    if (packetResult == MvError.MV_OK && packetSize > 0)
                    {
                        device.Parameters.SetIntValue("GevSCPSPacketSize", packetSize);
                    }
                }

                // 标定期间必须冻结曝光和增益，避免正/反 Gray Code 图卡阈值漂移。
                device.Parameters.SetEnumValueByString("ExposureAuto", "Off");
                device.Parameters.SetEnumValueByString("GainAuto", "Off");
                if (settings.ExposureTimeMicroseconds is double exposure)
                {
                    EnsureOk(device.Parameters.SetFloatValue("ExposureTime", (float)exposure), "设置曝光时间");
                }

                if (settings.Gain is double gain)
                {
                    EnsureOk(device.Parameters.SetFloatValue("Gain", (float)gain), "设置增益");
                }

                EnsureOk(device.Parameters.SetEnumValueByString("TriggerMode", "Off"), "关闭触发模式");
                device.StreamGrabber.SetImageNodeNum(4);
                EnsureOk(device.StreamGrabber.StartGrabbing(StreamGrabStrategy.LatestImages), "开始采集");

                _device = device;
                _grabbing = true;
                ConnectedCamera = descriptor;
            }
            catch
            {
                try { device.Close(); } catch { }
                device.Dispose();
                throw;
            }
        }
    }

    public Task<Bitmap> CaptureAsync(int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IDevice device = _device ?? throw new InvalidOperationException("相机尚未连接。");
                device.StreamGrabber.ClearImageBuffer();

                int result = device.StreamGrabber.GetImageBuffer((uint)timeoutMilliseconds, out IFrameOut frame);
                EnsureOk(result, "获取相机图像");
                string temporaryPath = Path.Combine(Path.GetTempPath(), $"distortion-capture-{Guid.NewGuid():N}.bmp");
                IFrameOut frameForSave = frame;
                try
                {
                    if (frame.Image.PixelType.ToString().Contains("HB", StringComparison.OrdinalIgnoreCase))
                    {
                        EnsureOk(device.ImageDecoder.HBDecode(frame, out frameForSave), "解码 HB 图像");
                    }

                    var format = new ImageFormatInfo { FormatType = ImageFormatType.Bmp };
                    EnsureOk(device.ImageSaver.SaveImageToFile(
                        temporaryPath, frameForSave.Image, format, CFAMethod.Equilibrated), "保存临时采集图");

                    using var loaded = new Bitmap(temporaryPath);
                    var raw = new Bitmap(loaded);
                    if (!_lensCorrection.Enabled) return raw;
                    try
                    {
                        Bitmap corrected = _lensCorrection.Correct(raw);
                        raw.Dispose();
                        return corrected;
                    }
                    catch
                    {
                        raw.Dispose();
                        throw;
                    }
                }
                finally
                {
                    if (!ReferenceEquals(frameForSave, frame)) frameForSave.Image.Dispose();
                    device.StreamGrabber.FreeImageBuffer(frame);
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                }
            }
        }, cancellationToken);
    }

    public void Disconnect()
    {
        lock (_sync) DisconnectCore();
    }

    private void DisconnectCore()
    {
        IDevice? device = _device;
        _device = null;
        ConnectedCamera = null;
        if (device is null) return;

        if (_grabbing)
        {
            try { device.StreamGrabber.StopGrabbing(); } catch { }
            _grabbing = false;
        }

        try { device.Close(); } catch { }
        device.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            DisconnectCore();
            _lensCorrection.Dispose();
            if (_sdkInitialized)
            {
                SDKSystem.Finalize();
                _sdkInitialized = false;
            }
        }
    }

    private static void EnsureOk(int result, string operation)
    {
        if (result != MvError.MV_OK)
        {
            throw new InvalidOperationException($"{operation}失败，MVS 错误码：0x{result:X8}");
        }
    }

    private static string FormatIp(uint value) =>
        $"{(value >> 24) & 0xff}.{(value >> 16) & 0xff}.{(value >> 8) & 0xff}.{value & 0xff}";
}
