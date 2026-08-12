using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AprilTagPose.Models;
using MvCameraControl;

namespace AprilTagPose.Camera;

/// <summary>
/// 一帧相机图像携带的海康 SDK 原始元数据。
/// </summary>
public sealed class CameraFrameMetadata
{
    internal CameraFrameMetadata(IFrameOut frame, DateTimeOffset capturedAt)
    {
        FrameNumber = frame.FrameNum;
        ExposureTime = frame.ExposureTime;
        Gain = frame.Gain;
        DeviceTimestamp = frame.DevTimeStamp;
        HostTimestamp = frame.HostTimeStamp;
        Width = frame.Image.Width;
        Height = frame.Image.Height;
        SourcePixelFormat = frame.Image.PixelType.ToString();
        CapturedAt = capturedAt;
    }

    public uint FrameNumber { get; }

    /// <summary>SDK 返回的曝光原值；通常为微秒，最终单位由相机节点定义。</summary>
    public float ExposureTime { get; }

    /// <summary>SDK 返回的增益原值。</summary>
    public float Gain { get; }

    /// <summary>SDK 返回的设备时间戳原值，未做单位换算。</summary>
    public ulong DeviceTimestamp { get; }

    /// <summary>SDK 返回的主机时间戳原值，未做单位换算。</summary>
    public ulong HostTimestamp { get; }

    public uint Width { get; }

    public uint Height { get; }

    public string SourcePixelFormat { get; }

    public DateTimeOffset CapturedAt { get; }
}

/// <summary>
/// 图像帧事件参数。事件订阅者取得该对象的所有权，使用完毕后必须调用 <see cref="Dispose"/>。
/// </summary>
public sealed class CameraFrameEventArgs : EventArgs, IDisposable
{
    private int _disposed;

    internal CameraFrameEventArgs(Bitmap image, CameraFrameMetadata metadata)
    {
        Image = image;
        Metadata = metadata;
    }

    public Bitmap Image { get; }

    public CameraFrameMetadata Metadata { get; }

    internal CameraFrameEventArgs CloneForSubscriber() =>
        new(new Bitmap(Image), Metadata);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Image.Dispose();
        }
    }
}

public sealed class CameraErrorEventArgs : EventArgs
{
    internal CameraErrorEventArgs(string message, int? errorCode, Exception? exception, bool isFatal)
    {
        Message = message;
        ErrorCode = errorCode;
        Exception = exception;
        IsFatal = isFatal;
    }

    public string Message { get; }

    public int? ErrorCode { get; }

    public Exception? Exception { get; }

    public bool IsFatal { get; }
}

public sealed class HikCameraException : Exception
{
    public HikCameraException(string message, int? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public int? ErrorCode { get; }
}

/// <summary>
/// 海康 MVS V2 相机封装。负责 SDK 生命周期、设备枚举、连续取流以及帧缓存释放。
/// </summary>
public sealed class HikCameraService : IDisposable, IAsyncDisposable
{
    private const uint FrameWaitTimeoutMilliseconds = 200;

    private const DeviceTLayerType SupportedTransportLayers =
        DeviceTLayerType.MvGigEDevice |
        DeviceTLayerType.MvUsbDevice |
        DeviceTLayerType.MvGenTLGigEDevice |
        DeviceTLayerType.MvGenTLCameraLinkDevice |
        DeviceTLayerType.MvGenTLCXPDevice |
        DeviceTLayerType.MvGenTLXoFDevice;

    private static readonly object SdkSync = new();
    private static int _sdkReferenceCount;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IDevice? _device;
    private CameraDescriptor? _currentCamera;
    private CancellationTokenSource? _grabCancellation;
    private Task? _grabTask;
    private int _nativeGrabbing;
    private int _disposeStarted;
    private int _frameDeliveryIntervalMilliseconds = 100;
    private long _lastFrameDeliveryTimestamp;

    public HikCameraService()
    {
        AcquireSdk();
    }

    /// <summary>
    /// 每个订阅者都会收到自己独立的 Bitmap，并负责释放事件参数。
    /// </summary>
    public event EventHandler<CameraFrameEventArgs>? FrameReceived;

    public event EventHandler<CameraErrorEventArgs>? ErrorOccurred;

    public bool IsOpen => _device is not null;

    public bool IsGrabbing => Volatile.Read(ref _nativeGrabbing) != 0;

    public CameraDescriptor? CurrentCamera => _currentCamera;

    public string SdkVersion => SDKSystem.GetSDKVersion();

    /// <summary>
    /// 向订阅者交付 Bitmap 的最小间隔。先在 SDK 缓冲阶段丢帧，避免高分辨率图像被无效深拷贝。
    /// </summary>
    public int FrameDeliveryIntervalMilliseconds
    {
        get => Volatile.Read(ref _frameDeliveryIntervalMilliseconds);
        set
        {
            if (value is < 0 or > 10_000)
                throw new ArgumentOutOfRangeException(nameof(value), "帧交付间隔必须在 0 到 10000 ms 之间。");
            Volatile.Write(ref _frameDeliveryIntervalMilliseconds, value);
        }
    }

    /// <summary>
    /// 枚举原生 GigE、USB3 及全部 MVS 支持的 GenTL 相机。
    /// </summary>
    public IReadOnlyList<CameraDescriptor> EnumerateDevices()
    {
        _lifecycleGate.Wait();
        try
        {
            ThrowIfDisposed();
            int result = DeviceEnumerator.EnumDevices(SupportedTransportLayers, out List<IDeviceInfo> deviceInfos);
            ThrowIfSdkFailed(result, "枚举相机");
            return deviceInfos.Select(static info => new CameraDescriptor(info)).ToArray();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Open(CameraDescriptor camera, CancellationToken cancellationToken = default) =>
        OpenAsync(camera, cancellationToken).GetAwaiter().GetResult();

    public async Task OpenAsync(CameraDescriptor camera, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_device is not null)
            {
                throw new InvalidOperationException("已有相机处于打开状态，请先关闭当前相机。");
            }

            IDevice? candidate = null;
            try
            {
                candidate = DeviceFactory.CreateDevice(camera.NativeDeviceInfo);
                int result = candidate.Open();
                ThrowIfSdkFailed(result, $"打开相机 {camera.DisplayName}");

                ConfigureGigEPacketSize(candidate);
                ConfigureContinuousAcquisition(candidate);

                _device = candidate;
                _currentCamera = camera;
                candidate = null;
            }
            catch (HikCameraException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new HikCameraException(
                    $"创建或打开相机“{camera.DisplayName}”失败：{exception.Message}",
                    innerException: exception);
            }
            finally
            {
                if (candidate is not null)
                {
                    TryCloseAndDispose(candidate);
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void StartGrabbing(CancellationToken cancellationToken = default) =>
        StartGrabbingAsync(cancellationToken).GetAwaiter().GetResult();

    public async Task StartGrabbingAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            IDevice device = _device ?? throw new InvalidOperationException("尚未打开相机，无法开始取流。");

            if (_grabTask is { IsCompleted: false })
            {
                return;
            }

            CleanupCompletedGrabTask();
            cancellationToken.ThrowIfCancellationRequested();

            int result = device.StreamGrabber.StartGrabbing();
            ThrowIfSdkFailed(result, "开始相机取流");

            Volatile.Write(ref _nativeGrabbing, 1);
            Volatile.Write(ref _lastFrameDeliveryTimestamp, 0);
            _grabCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken grabToken = _grabCancellation.Token;
            _grabTask = Task.Run(() => CaptureLoop(device, grabToken), CancellationToken.None);
        }
        catch
        {
            if (_grabTask is null)
            {
                StopNativeGrabbing(_device, reportFailure: false);
            }

            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void StopGrabbing() => StopGrabbingAsync().GetAwaiter().GetResult();

    public async Task StopGrabbingAsync()
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await StopGrabbingWhileLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Close() => CloseAsync().GetAwaiter().GetResult();

    public async Task CloseAsync()
    {
        ThrowIfDisposed();
        await CloseCoreAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await CloseCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _grabCancellation?.Dispose();
            _lifecycleGate.Dispose();
            ReleaseSdk();
            GC.SuppressFinalize(this);
        }
    }

    private async Task CloseCoreAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopGrabbingWhileLockedAsync().ConfigureAwait(false);

            IDevice? device = _device;
            _device = null;
            _currentCamera = null;
            if (device is null)
            {
                return;
            }

            int closeResult = MvError.MV_OK;
            try
            {
                closeResult = device.Close();
            }
            catch (Exception exception)
            {
                ReportError($"关闭相机时发生异常：{exception.Message}", null, exception, isFatal: false);
            }
            finally
            {
                device.Dispose();
            }

            if (closeResult != MvError.MV_OK)
            {
                ReportSdkError("关闭相机", closeResult, isFatal: false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopGrabbingWhileLockedAsync()
    {
        CancellationTokenSource? cancellation = _grabCancellation;
        Task? grabTask = _grabTask;
        cancellation?.Cancel();

        if (grabTask is not null)
        {
            try
            {
                await grabTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消是正常停止路径。
            }
            catch (Exception exception)
            {
                ReportError($"相机取流任务异常结束：{exception.Message}", null, exception, isFatal: false);
            }
        }

        StopNativeGrabbing(_device, reportFailure: true);
        _grabTask = null;
        _grabCancellation = null;
        cancellation?.Dispose();
    }

    private void CaptureLoop(IDevice device, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IFrameOut? frame = null;
                int result;
                try
                {
                    result = device.StreamGrabber.GetImageBuffer(FrameWaitTimeoutMilliseconds, out frame);
                }
                catch (Exception exception)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ReportError($"从相机读取图像时发生异常：{exception.Message}", null, exception, isFatal: true);
                    }

                    break;
                }

                if (result == MvError.MV_E_NODATA)
                {
                    continue;
                }

                if (result != MvError.MV_OK)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        ReportSdkError("从相机获取图像", result, isFatal: true);
                    }

                    break;
                }

                if (frame is null)
                {
                    ReportError("海康 SDK 返回成功，但图像帧对象为空。", null, null, isFatal: true);
                    break;
                }

                Bitmap? image = null;
                try
                {
                    if (!ShouldDeliverFrame())
                    {
                        continue;
                    }

                    DateTimeOffset capturedAt = DateTimeOffset.Now;
                    image = CreateOwnedBitmap(device, frame);
                    var metadata = new CameraFrameMetadata(frame, capturedAt);
                    var delivery = new CameraFrameEventArgs(image, metadata);
                    image = null; // 所有权已经移交给事件参数。
                    try
                    {
                        _ = Task.Run(() => DispatchFrame(delivery));
                    }
                    catch
                    {
                        delivery.Dispose();
                        throw;
                    }
                }
                catch (Exception exception)
                {
                    ReportError($"转换相机图像失败：{exception.Message}", null, exception, isFatal: false);
                }
                finally
                {
                    image?.Dispose();
                    int freeResult;
                    try
                    {
                        freeResult = device.StreamGrabber.FreeImageBuffer(frame);
                    }
                    catch (Exception exception)
                    {
                        ReportError($"释放海康图像缓存时发生异常：{exception.Message}", null, exception, isFatal: false);
                        freeResult = MvError.MV_OK;
                    }

                    if (freeResult != MvError.MV_OK)
                    {
                        ReportSdkError("释放海康图像缓存", freeResult, isFatal: false);
                    }
                }
            }
        }
        finally
        {
            StopNativeGrabbing(device, reportFailure: !cancellationToken.IsCancellationRequested);
        }
    }

    private static Bitmap CreateOwnedBitmap(IDevice device, IFrameOut frame)
    {
        IImage source = frame.Image ?? throw new InvalidOperationException("图像帧没有有效的 Image 对象。");
        IImage? converted = null;
        try
        {
            Bitmap sdkBitmap;
            if (HikImageBitmapExtensions.CanConvertDirectly(source.PixelType))
            {
                sdkBitmap = frame.Image.ToBitmap();
            }
            else
            {
                int result = device.PixelTypeConverter.ConvertPixelType(
                    source,
                    out converted,
                    MvGvspPixelType.PixelType_Gvsp_BGR8_Packed);
                ThrowIfSdkFailed(result, $"将像素格式 {source.PixelType} 转换为 BGR8");
                if (converted is null)
                {
                    throw new InvalidOperationException("海康 SDK 像素格式转换成功，但输出图像为空。");
                }

                sdkBitmap = converted.ToBitmap();
            }

            // 内部 ToBitmap 已逐行复制到 GDI+ 自有内存，可直接把所有权交给帧事件。
            return sdkBitmap;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private bool ShouldDeliverFrame()
    {
        int intervalMilliseconds = Volatile.Read(ref _frameDeliveryIntervalMilliseconds);
        if (intervalMilliseconds <= 0) return true;

        long now = Stopwatch.GetTimestamp();
        long previous = Volatile.Read(ref _lastFrameDeliveryTimestamp);
        long requiredTicks = intervalMilliseconds * Stopwatch.Frequency / 1000L;
        if (previous != 0 && now - previous < requiredTicks) return false;
        Volatile.Write(ref _lastFrameDeliveryTimestamp, now);
        return true;
    }

    private void DispatchFrame(CameraFrameEventArgs frame)
    {
        EventHandler<CameraFrameEventArgs>? handlers = FrameReceived;
        if (handlers is null)
        {
            frame.Dispose();
            return;
        }

        Delegate[] subscribers = handlers.GetInvocationList();
        for (int index = 0; index < subscribers.Length; index++)
        {
            CameraFrameEventArgs delivery;
            try
            {
                delivery = index == subscribers.Length - 1
                    ? frame
                    : frame.CloneForSubscriber();
            }
            catch (Exception exception)
            {
                frame.Dispose();
                ReportError($"为图像帧订阅者创建副本失败：{exception.Message}", null, exception, isFatal: false);
                return;
            }

            try
            {
                ((EventHandler<CameraFrameEventArgs>)subscribers[index])(this, delivery);
            }
            catch (Exception exception)
            {
                delivery.Dispose();
                ReportError($"图像帧事件处理程序发生异常：{exception.Message}", null, exception, isFatal: false);
            }
        }
    }

    private void ConfigureGigEPacketSize(IDevice device)
    {
        if (device is not IGigEDevice gigEDevice)
        {
            return;
        }

        int result = gigEDevice.GetOptimalPacketSize(out int packetSize);
        if (result != MvError.MV_OK)
        {
            ReportSdkError("获取 GigE 最佳网络包大小", result, isFatal: false);
            return;
        }

        result = device.Parameters.SetIntValue("GevSCPSPacketSize", packetSize);
        if (result != MvError.MV_OK)
        {
            ReportSdkError($"设置 GigE 网络包大小为 {packetSize}", result, isFatal: false);
        }
    }

    private static void ConfigureContinuousAcquisition(IDevice device)
    {
        int result = device.Parameters.SetEnumValueByString("AcquisitionMode", "Continuous");
        ThrowIfSdkFailed(result, "设置连续采集模式");

        result = device.Parameters.SetEnumValueByString("TriggerMode", "Off");
        ThrowIfSdkFailed(result, "关闭相机触发模式");
    }

    private void CleanupCompletedGrabTask()
    {
        if (_grabTask is not { IsCompleted: true })
        {
            return;
        }

        _grabTask = null;
        _grabCancellation?.Dispose();
        _grabCancellation = null;
        StopNativeGrabbing(_device, reportFailure: true);
    }

    private void StopNativeGrabbing(IDevice? device, bool reportFailure)
    {
        if (Interlocked.Exchange(ref _nativeGrabbing, 0) == 0 || device is null)
        {
            return;
        }

        try
        {
            int result = device.StreamGrabber.StopGrabbing();
            if (reportFailure && result != MvError.MV_OK)
            {
                ReportSdkError("停止相机取流", result, isFatal: false);
            }
        }
        catch (Exception exception)
        {
            if (reportFailure)
            {
                ReportError($"停止相机取流时发生异常：{exception.Message}", null, exception, isFatal: false);
            }
        }
    }

    private static void TryCloseAndDispose(IDevice device)
    {
        try
        {
            device.Close();
        }
        catch
        {
            // 保留原始打开/配置异常。
        }
        finally
        {
            device.Dispose();
        }
    }

    private void ReportSdkError(string operation, int errorCode, bool isFatal) =>
        ReportError(FormatSdkError(operation, errorCode), errorCode, null, isFatal);

    private void ReportError(string message, int? errorCode, Exception? exception, bool isFatal)
    {
        EventHandler<CameraErrorEventArgs>? handlers = ErrorOccurred;
        if (handlers is null)
        {
            return;
        }

        var eventArgs = new CameraErrorEventArgs(message, errorCode, exception, isFatal);
        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<CameraErrorEventArgs>)subscriber)(this, eventArgs);
            }
            catch
            {
                // 错误通知不能反向中断 SDK 清理路径。
            }
        }
    }

    private static void ThrowIfSdkFailed(int result, string operation)
    {
        if (result != MvError.MV_OK)
        {
            throw new HikCameraException(FormatSdkError(operation, result), result);
        }
    }

    private static string FormatSdkError(string operation, int errorCode)
    {
        string description = errorCode switch
        {
            MvError.MV_E_HANDLE => "句柄无效",
            MvError.MV_E_SUPPORT => "当前设备不支持此功能",
            MvError.MV_E_BUFOVER => "图像缓存已满",
            MvError.MV_E_CALLORDER => "SDK 调用顺序错误",
            MvError.MV_E_PARAMETER => "参数错误",
            MvError.MV_E_RESOURCE => "申请资源失败",
            MvError.MV_E_NODATA => "在等待时间内没有收到图像",
            MvError.MV_E_PRECONDITION => "前置条件不满足或运行环境已改变",
            MvError.MV_E_VERSION => "SDK 版本不匹配",
            MvError.MV_E_NOENOUGH_BUF => "内存不足",
            MvError.MV_E_GC_ACCESS => "相机节点当前不可访问",
            MvError.MV_E_ACCESS_DENIED => "没有设备访问权限",
            MvError.MV_E_BUSY => "设备正忙或网络已断开",
            MvError.MV_E_NETER => "网络错误",
            MvError.MV_E_DEV_DISCONNECT => "相机已断开连接",
            MvError.MV_E_USB_DEVICE => "USB 设备异常",
            MvError.MV_E_USB_DRIVER => "USB 驱动异常",
            MvError.MV_E_UNKNOW => "未知错误",
            _ => "未分类的 SDK 错误"
        };

        return $"{operation}失败：{description}（错误码 0x{unchecked((uint)errorCode):X8}）。";
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

    private static void AcquireSdk()
    {
        lock (SdkSync)
        {
            if (_sdkReferenceCount == 0)
            {
                int result = SDKSystem.Initialize();
                ThrowIfSdkFailed(result, "初始化海康 MVS SDK");
            }

            checked
            {
                _sdkReferenceCount++;
            }
        }
    }

    private static void ReleaseSdk()
    {
        lock (SdkSync)
        {
            if (_sdkReferenceCount <= 0)
            {
                return;
            }

            _sdkReferenceCount--;
            if (_sdkReferenceCount == 0)
            {
                SDKSystem.Finalize();
            }
        }
    }
}

/// <summary>
/// MVS V2 4.8 的 IImage 没有公开 ToBitmap；此扩展只接收可无损映射到 GDI+ 的格式。
/// 其他相机格式由 HikCameraService 先通过 IPixelTypeConverter 转为 BGR8。
/// </summary>
internal static class HikImageBitmapExtensions
{
    internal static bool CanConvertDirectly(MvGvspPixelType pixelType) => pixelType is
        MvGvspPixelType.PixelType_Gvsp_Mono8 or
        MvGvspPixelType.PixelType_Gvsp_BGR8_Packed or
        MvGvspPixelType.PixelType_Gvsp_RGB8_Packed or
        MvGvspPixelType.PixelType_Gvsp_BGRA8_Packed or
        MvGvspPixelType.PixelType_Gvsp_RGBA8_Packed;

    internal static Bitmap ToBitmap(this IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        int width = checked((int)image.Width);
        int height = checked((int)image.Height);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"相机图像尺寸无效：{width} × {height}。");
        }

        PixelFormat bitmapFormat;
        int bytesPerPixel;
        bool swapRedAndBlue;
        switch (image.PixelType)
        {
            case MvGvspPixelType.PixelType_Gvsp_Mono8:
                bitmapFormat = PixelFormat.Format8bppIndexed;
                bytesPerPixel = 1;
                swapRedAndBlue = false;
                break;
            case MvGvspPixelType.PixelType_Gvsp_BGR8_Packed:
                bitmapFormat = PixelFormat.Format24bppRgb;
                bytesPerPixel = 3;
                swapRedAndBlue = false;
                break;
            case MvGvspPixelType.PixelType_Gvsp_RGB8_Packed:
                bitmapFormat = PixelFormat.Format24bppRgb;
                bytesPerPixel = 3;
                swapRedAndBlue = true;
                break;
            case MvGvspPixelType.PixelType_Gvsp_BGRA8_Packed:
                bitmapFormat = PixelFormat.Format32bppArgb;
                bytesPerPixel = 4;
                swapRedAndBlue = false;
                break;
            case MvGvspPixelType.PixelType_Gvsp_RGBA8_Packed:
                bitmapFormat = PixelFormat.Format32bppArgb;
                bytesPerPixel = 4;
                swapRedAndBlue = true;
                break;
            default:
                throw new NotSupportedException($"像素格式 {image.PixelType} 不能直接转换为 Bitmap。");
        }

        int sourceStride = checked(width * bytesPerPixel);
        long requiredBytes = checked((long)sourceStride * height);
        if ((ulong)requiredBytes > image.ImageSize)
        {
            throw new InvalidOperationException(
                $"相机图像缓存长度不足：需要 {requiredBytes} 字节，实际为 {image.ImageSize} 字节。");
        }

        var bitmap = new Bitmap(width, height, bitmapFormat);
        try
        {
            if (bitmapFormat == PixelFormat.Format8bppIndexed)
            {
                ColorPalette palette = bitmap.Palette;
                for (int value = 0; value < palette.Entries.Length; value++)
                {
                    palette.Entries[value] = Color.FromArgb(value, value, value);
                }

                bitmap.Palette = palette;
            }

            Rectangle bounds = new(0, 0, width, height);
            BitmapData bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, bitmapFormat);
            try
            {
                CopyRows(image, bitmapData, sourceStride, height, bytesPerPixel, swapRedAndBlue);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void CopyRows(
        IImage image,
        BitmapData destination,
        int sourceStride,
        int height,
        int bytesPerPixel,
        bool swapRedAndBlue)
    {
        byte[] row = new byte[sourceStride];
        IntPtr sourcePointer = image.PixelDataPtr;
        byte[]? managedSource = sourcePointer == IntPtr.Zero ? image.PixelData : null;

        for (int y = 0; y < height; y++)
        {
            int sourceOffset = checked(y * sourceStride);
            if (sourcePointer != IntPtr.Zero)
            {
                Marshal.Copy(IntPtr.Add(sourcePointer, sourceOffset), row, 0, sourceStride);
            }
            else
            {
                if (managedSource is null || managedSource.Length < sourceOffset + sourceStride)
                {
                    throw new InvalidOperationException("无法读取完整的相机图像缓存。");
                }

                Buffer.BlockCopy(managedSource, sourceOffset, row, 0, sourceStride);
            }

            if (swapRedAndBlue)
            {
                for (int x = 0; x < row.Length; x += bytesPerPixel)
                {
                    (row[x], row[x + 2]) = (row[x + 2], row[x]);
                }
            }

            Marshal.Copy(row, 0, IntPtr.Add(destination.Scan0, y * destination.Stride), sourceStride);
        }
    }
}
