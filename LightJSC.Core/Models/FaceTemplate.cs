namespace LightJSC.Core.Models;

public sealed class FaceTemplate
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public byte[] FeatureBytes { get; set; } = Array.Empty<byte>();
    public float L2Norm { get; set; }
    public string FeatureVersion { get; set; } = string.Empty;
    public byte[]? FaceImageJpeg { get; set; }
    public string? SourceCameraId { get; set; }
    public string FeatureHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

