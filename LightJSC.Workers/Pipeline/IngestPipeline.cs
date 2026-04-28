using System.Threading.Channels;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Options;

namespace LightJSC.Workers.Pipeline;

public sealed class IngestPipeline
{
    private long _faceEventQueueLength;
    private long _decisionQueueLength;
    private long _lastMetadataReceivedTicks;
    private long _lastFaceEventEnqueuedTicks;
    private long _lastFaceEventDequeuedTicks;
    private long _lastDecisionEnqueuedTicks;
    private long _lastDecisionDequeuedTicks;

    public IngestPipeline(IOptions<IngestOptions> options)
    {
        var capacity = options.Value.ChannelCapacity;
        FaceEvents = Channel.CreateBounded<FaceEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        });

        Decisions = Channel.CreateBounded<FaceMatchDecision>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public Channel<FaceEvent> FaceEvents { get; }
    public Channel<FaceMatchDecision> Decisions { get; }

    public long FaceEventQueueLength => Interlocked.Read(ref _faceEventQueueLength);
    public long DecisionQueueLength => Interlocked.Read(ref _decisionQueueLength);
    public DateTimeOffset? LastMetadataReceivedUtc => FromTicks(Interlocked.Read(ref _lastMetadataReceivedTicks));
    public DateTimeOffset? LastFaceEventEnqueuedUtc => FromTicks(Interlocked.Read(ref _lastFaceEventEnqueuedTicks));
    public DateTimeOffset? LastFaceEventDequeuedUtc => FromTicks(Interlocked.Read(ref _lastFaceEventDequeuedTicks));
    public DateTimeOffset? LastDecisionEnqueuedUtc => FromTicks(Interlocked.Read(ref _lastDecisionEnqueuedTicks));
    public DateTimeOffset? LastDecisionDequeuedUtc => FromTicks(Interlocked.Read(ref _lastDecisionDequeuedTicks));

    public void RecordMetadataReceived(DateTimeOffset receivedAtUtc)
    {
        Interlocked.Exchange(ref _lastMetadataReceivedTicks, receivedAtUtc.UtcDateTime.Ticks);
    }

    public bool TryEnqueueFaceEvent(FaceEvent faceEvent)
    {
        if (FaceEvents.Writer.TryWrite(faceEvent))
        {
            Interlocked.Increment(ref _faceEventQueueLength);
            Interlocked.Exchange(ref _lastFaceEventEnqueuedTicks, DateTime.UtcNow.Ticks);
            return true;
        }

        return false;
    }

    public bool TryEnqueueDecision(FaceMatchDecision decision)
    {
        if (Decisions.Writer.TryWrite(decision))
        {
            Interlocked.Increment(ref _decisionQueueLength);
            Interlocked.Exchange(ref _lastDecisionEnqueuedTicks, DateTime.UtcNow.Ticks);
            return true;
        }

        return false;
    }

    public void DecrementFaceEventQueue()
    {
        Interlocked.Decrement(ref _faceEventQueueLength);
        Interlocked.Exchange(ref _lastFaceEventDequeuedTicks, DateTime.UtcNow.Ticks);
    }

    public void DecrementDecisionQueue()
    {
        Interlocked.Decrement(ref _decisionQueueLength);
        Interlocked.Exchange(ref _lastDecisionDequeuedTicks, DateTime.UtcNow.Ticks);
    }

    private static DateTimeOffset? FromTicks(long ticks)
    {
        if (ticks <= 0)
        {
            return null;
        }

        return new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
    }
}

