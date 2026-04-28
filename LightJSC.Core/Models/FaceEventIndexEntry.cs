namespace LightJSC.Core.Models;

public sealed class FaceEventIndexEntry
{
    public Guid EventId { get; init; }
    public DateTime EventTimeUtc { get; init; }
    public string CameraId { get; init; } = string.Empty;
    public string FeatureVersion { get; init; } = string.Empty;
    public float[] FeatureVector { get; init; } = Array.Empty<float>();
}
