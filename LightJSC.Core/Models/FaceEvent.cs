namespace LightJSC.Core.Models;

public sealed class FaceEvent
{
    public string CameraId { get; init; } = string.Empty;
    public string? CameraCode { get; init; }
    public string CameraIp { get; init; } = string.Empty;
    public string? CameraName { get; init; }
    public DateTimeOffset EventTimeUtc { get; init; }
    public byte[] FeatureBytes { get; init; } = Array.Empty<byte>();
    public float[] FeatureVector { get; init; } = Array.Empty<float>();
    public float L2Norm { get; init; }
    public string FeatureVersion { get; set; } = string.Empty;
    public int? Age { get; init; }
    public string? Gender { get; init; }
    public string? Mask { get; init; }
    public string? FaceImageBase64 { get; init; }
    public string? BsFrame { get; init; }
    public string? ThumbFrame { get; init; }
    public float? Score { get; init; }
    public BoundingBox? BBox { get; init; }
}

