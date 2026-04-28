namespace LightJSC.Core.Models;

public sealed record VectorMatch(string EntryId, float Similarity, float Threshold, WatchlistEntry? Entry);

