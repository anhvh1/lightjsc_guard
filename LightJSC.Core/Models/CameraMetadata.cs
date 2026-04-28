namespace LightJSC.Core.Models;

public sealed class CameraMetadata
{
    public string CameraId { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string? CameraCode { get; init; }
    public string? CameraName { get; init; }
}
