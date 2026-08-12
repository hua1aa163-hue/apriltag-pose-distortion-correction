using DistortionCorrection.Models;

namespace DistortionCorrection.Camera;

public interface ICameraService : IDisposable
{
    bool IsConnected { get; }
    CameraDescriptor? ConnectedCamera { get; }
    IReadOnlyList<CameraDescriptor> Enumerate();
    void Connect(CameraDescriptor descriptor, AppSettings settings);
    void Disconnect();
    Task<Bitmap> CaptureAsync(int timeoutMilliseconds, CancellationToken cancellationToken);
}
