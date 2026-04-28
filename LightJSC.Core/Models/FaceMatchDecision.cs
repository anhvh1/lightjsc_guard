namespace LightJSC.Core.Models;

public sealed class FaceMatchDecision
{
    public FaceEvent FaceEvent { get; init; } = new();
    public bool IsKnown { get; init; }
    public float? Similarity { get; init; }
    public string? WatchlistEntryId { get; init; }
    public string? PersonId { get; init; }
    public PersonProfile? Person { get; init; }
    public float Threshold { get; init; }
}

