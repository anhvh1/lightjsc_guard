using System.Collections.Concurrent;
using System.Linq;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;

namespace LightJSC.Infrastructure.Vector;

public sealed class BruteForceVectorIndex : IVectorIndex
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WatchlistEntry>> _entriesByModel = new();

    public int Count => _entriesByModel.Values.Sum(entries => entries.Count);

    public void AddOrUpdate(WatchlistEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.EntryId))
        {
            return;
        }

        var modelKey = VectorIndexNaming.NormalizeFeatureVersion(entry.FeatureVersion);
        var entries = _entriesByModel.GetOrAdd(modelKey, _ => new ConcurrentDictionary<string, WatchlistEntry>());
        entries[entry.EntryId] = entry;
    }

    public bool Remove(string entryId)
    {
        var removed = false;
        foreach (var pair in _entriesByModel)
        {
            if (pair.Value.TryRemove(entryId, out _))
            {
                removed = true;
            }

            if (pair.Value.IsEmpty)
            {
                _entriesByModel.TryRemove(pair.Key, out _);
            }
        }

        return removed;
    }

    public IReadOnlyList<VectorMatch> SearchTopK(float[] vector, string? featureVersion, int topK)
    {
        if (vector.Length == 0 || topK <= 0)
        {
            return Array.Empty<VectorMatch>();
        }

        var modelKey = VectorIndexNaming.NormalizeFeatureVersion(featureVersion);
        if (!_entriesByModel.TryGetValue(modelKey, out var entries))
        {
            return Array.Empty<VectorMatch>();
        }

        var results = new List<VectorMatch>();
        foreach (var entry in entries.Values)
        {
            if (!entry.IsActive || entry.FeatureVector.Length != vector.Length)
            {
                continue;
            }

            var similarity = VectorMath.DotProduct(vector, entry.FeatureVector);
            results.Add(new VectorMatch(entry.EntryId, similarity, entry.SimilarityThreshold, entry));
        }

        return results.OrderByDescending(x => x.Similarity).Take(topK).ToList();
    }
}

