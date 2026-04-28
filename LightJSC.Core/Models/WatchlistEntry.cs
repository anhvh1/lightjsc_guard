namespace LightJSC.Core.Models;

public sealed class WatchlistEntry
{
    public string EntryId { get; init; } = string.Empty;
    public string? PersonId { get; init; }
    public PersonProfile? Person { get; init; }
    public string FeatureVersion { get; init; } = string.Empty;
    public float[] FeatureVector { get; init; } = Array.Empty<float>();
    public byte[] FeatureBytes { get; init; } = Array.Empty<byte>();
    public float SimilarityThreshold { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool IsActive { get; init; } = true;
}

