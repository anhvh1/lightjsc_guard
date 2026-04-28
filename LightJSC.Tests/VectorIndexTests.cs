using LightJSC.Core.Models;
using LightJSC.Infrastructure.Vector;
using Xunit;

namespace LightJSC.Tests;

public sealed class VectorIndexTests
{
    [Fact]
    public void SearchTopKReturnsClosestVector()
    {
        var index = new BruteForceVectorIndex();
        index.AddOrUpdate(new WatchlistEntry
        {
            EntryId = "A",
            FeatureVersion = "0.1",
            FeatureVector = new[] { 1f, 0f },
            SimilarityThreshold = 0.5f,
            IsActive = true
        });
        index.AddOrUpdate(new WatchlistEntry
        {
            EntryId = "B",
            FeatureVersion = "0.1",
            FeatureVector = new[] { 0f, 1f },
            SimilarityThreshold = 0.5f,
            IsActive = true
        });

        var results = index.SearchTopK(new[] { 0.9f, 0.1f }, "0.1", 1);

        Assert.Single(results);
        Assert.Equal("A", results[0].EntryId);
    }
}

