using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Processing;
using Microsoft.Extensions.Options;
using Xunit;

namespace LightJSC.Tests;

public sealed class DeduplicatorTests
{
    [Fact]
    public void DeduplicatorDropsDuplicateWithinWindow()
    {
        var options = Options.Create(new IngestOptions
        {
            BestShotOnly = false,
            DedupWindowMs = 1500,
            ChannelCapacity = 10
        });

        var deduplicator = new FaceEventDeduplicator(options);
        var bytes = new byte[] { 1, 2, 3, 4 };
        var faceEvent = new FaceEvent
        {
            CameraId = "CAM01",
            EventTimeUtc = DateTimeOffset.UtcNow,
            FeatureBytes = bytes,
            FeatureVector = new[] { 1f }
        };

        Assert.True(deduplicator.ShouldProcess(faceEvent));
        Assert.False(deduplicator.ShouldProcess(faceEvent));
    }
}

