using System.Collections.Concurrent;
using System.Security.Cryptography;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Options;

namespace LightJSC.Infrastructure.Processing;

public sealed class FaceEventDeduplicator : IFaceEventDeduplicator
{
    private readonly IngestOptions _options;
    private readonly ConcurrentDictionary<string, long> _seen = new();
    private readonly int _pruneThreshold;
    private int _calls;

    public FaceEventDeduplicator(IOptions<IngestOptions> options)
    {
        _options = options.Value;
        _pruneThreshold = Math.Max(_options.ChannelCapacity * 4, 5000);
    }

    public bool ShouldProcess(FaceEvent faceEvent)
    {
        if (_options.BestShotOnly
            && string.IsNullOrWhiteSpace(faceEvent.BsFrame)
            && string.IsNullOrWhiteSpace(faceEvent.ThumbFrame)
            && string.IsNullOrWhiteSpace(faceEvent.FaceImageBase64))
        {
            return false;
        }

        if (faceEvent.FeatureBytes.Length == 0)
        {
            return false;
        }

        var hash = Convert.ToHexString(SHA256.HashData(faceEvent.FeatureBytes));
        var bucket = faceEvent.EventTimeUtc.ToUnixTimeMilliseconds() / _options.DedupWindowMs;
        var key = string.Concat(faceEvent.CameraId, ":", hash, ":", bucket.ToString());

        var nowTicks = DateTime.UtcNow.Ticks;
        if (_seen.TryAdd(key, nowTicks))
        {
            PruneIfNeeded(nowTicks);
            return true;
        }

        return false;
    }

    private void PruneIfNeeded(long nowTicks)
    {
        var current = Interlocked.Increment(ref _calls);
        if (current % 100 != 0 && _seen.Count < _pruneThreshold)
        {
            return;
        }

        var cutoff = nowTicks - TimeSpan.FromSeconds(30).Ticks;
        foreach (var item in _seen)
        {
            if (item.Value < cutoff)
            {
                _seen.TryRemove(item.Key, out _);
            }
        }
    }
}

