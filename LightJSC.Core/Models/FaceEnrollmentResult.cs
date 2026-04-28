namespace LightJSC.Core.Models;

public sealed class FaceEnrollmentResult
{
    public byte[] FeatureBytes { get; init; } = Array.Empty<byte>();
    public float[] FeatureVector { get; init; } = Array.Empty<float>();
    public float L2Norm { get; init; }
}

