namespace DistortionCorrection.Models;

public sealed record CameraDescriptor(
    string Id,
    string ModelName,
    string SerialNumber,
    string IpAddress,
    string HostAdapterIp)
{
    public override string ToString() => string.IsNullOrWhiteSpace(IpAddress)
        ? $"{ModelName} ({SerialNumber})"
        : $"{ModelName} ({SerialNumber}) [{IpAddress}]";
}
