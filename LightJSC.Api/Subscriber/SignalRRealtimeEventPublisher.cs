using System.Security.Cryptography;
using System.Threading.Channels;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Subscriber;

public sealed class SignalRRealtimeEventPublisher : BackgroundService, IRealtimeEventPublisher
{
    private readonly FaceEventBuffer _buffer;
    private readonly IHubContext<FaceEventsHub> _hub;
    private readonly IOptionsMonitor<SubscriberServiceOptions> _options;
    private readonly ILogger<SignalRRealtimeEventPublisher> _logger;
    private readonly Channel<FaceEventDto> _queue;

    public SignalRRealtimeEventPublisher(
        FaceEventBuffer buffer,
        IHubContext<FaceEventsHub> hub,
        IOptionsMonitor<SubscriberServiceOptions> options,
        ILogger<SignalRRealtimeEventPublisher> logger)
    {
        _buffer = buffer;
        _hub = hub;
        _options = options;
        _logger = logger;
        var capacity = Math.Max(128, options.CurrentValue.MaxItems * 10);
        _queue = Channel.CreateBounded<FaceEventDto>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public bool TryPublish(FaceMatchDecision decision)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return false;
        }

        var dto = MapDecision(decision);
        _buffer.Add(dto);
        if (_queue.Writer.TryWrite(dto))
        {
            return true;
        }

        _logger.LogDebug("Realtime broadcast queue full; dropping event {EventId}", dto.Id);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var faceEvent in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _hub.Clients.All.SendAsync("faceEvent", faceEvent, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Realtime broadcast failed for event {EventId}", faceEvent.Id);
            }
        }
    }

    private static FaceEventDto MapDecision(FaceMatchDecision decision)
    {
        var face = decision.FaceEvent;
        var eventTime = face.EventTimeUtc == default ? DateTimeOffset.UtcNow : face.EventTimeUtc;
        var featureBase64 = face.FeatureBytes.Length == 0 ? null : Convert.ToBase64String(face.FeatureBytes);

        return new FaceEventDto
        {
            Id = BuildIdempotencyKey(face),
            CameraId = face.CameraId,
            CameraCode = string.IsNullOrWhiteSpace(face.CameraCode) ? null : face.CameraCode,
            CameraIp = string.IsNullOrWhiteSpace(face.CameraIp) ? null : face.CameraIp,
            CameraName = face.CameraName,
            Zone = null,
            EventTimeUtc = eventTime,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            FeatureBase64 = featureBase64,
            L2Norm = face.L2Norm,
            FeatureVersion = face.FeatureVersion,
            Age = face.Age,
            Gender = face.Gender,
            Mask = face.Mask,
            ScoreText = face.Score.HasValue ? face.Score.Value.ToString("0.000") : null,
            SimilarityText = decision.Similarity.HasValue ? decision.Similarity.Value.ToString("0.000") : null,
            WatchlistEntryId = decision.WatchlistEntryId,
            PersonId = decision.PersonId,
            Person = decision.Person,
            BBox = face.BBox,
            IsKnown = decision.IsKnown,
            FaceImageBase64 = face.FaceImageBase64
        };
    }

    private static string BuildIdempotencyKey(FaceEvent faceEvent)
    {
        var hash = Convert.ToHexString(SHA256.HashData(faceEvent.FeatureBytes));
        return string.Concat(faceEvent.CameraId, ":", faceEvent.EventTimeUtc.ToUnixTimeMilliseconds().ToString(), ":", hash);
    }
}
