using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IVectorIndex
{
    void AddOrUpdate(WatchlistEntry entry);
    bool Remove(string entryId);
    IReadOnlyList<VectorMatch> SearchTopK(float[] vector, string? featureVersion, int topK);
    int Count { get; }
}

