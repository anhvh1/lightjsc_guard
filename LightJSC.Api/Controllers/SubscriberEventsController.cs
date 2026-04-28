using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LightJSC.Api.Subscriber;
using LightJSC.Core.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

/// <summary>
/// In-process subscriber service for realtime face events.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class SubscriberEventsController : ControllerBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly FaceEventBuffer _buffer;
    private readonly SignatureVerifier _signatureVerifier;
    private readonly IMemoryCache _cache;
    private readonly IHubContext<FaceEventsHub> _hub;
    private readonly IOptionsMonitor<SubscriberServiceOptions> _options;
    private readonly ILogger<SubscriberEventsController> _logger;

    public SubscriberEventsController(
        FaceEventBuffer buffer,
        SignatureVerifier signatureVerifier,
        IMemoryCache cache,
        IHubContext<FaceEventsHub> hub,
        IOptionsMonitor<SubscriberServiceOptions> options,
        ILogger<SubscriberEventsController> logger)
    {
        _buffer = buffer;
        _signatureVerifier = signatureVerifier;
        _cache = cache;
        _hub = hub;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Returns the latest known/unknown face events snapshot.
    /// </summary>
    [HttpGet("events")]
    public ActionResult<FaceEventSnapshot> GetSnapshot()
    {
        if (!IsEnabled())
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Subscriber service is disabled.");
        }

        return Ok(_buffer.GetSnapshot());
    }

    /// <summary>
    /// Receive face webhook events for realtime streaming.
    /// </summary>
    [HttpPost("webhooks/face")]
    public async Task<IActionResult> ReceiveFaceWebhook(CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Subscriber service is disabled.");
        }

        HttpContext.Request.EnableBuffering();
        using var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        HttpContext.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest("Payload is empty.");
        }

        if (!_signatureVerifier.IsValid(HttpContext, body))
        {
            return Unauthorized();
        }

        FaceWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FaceWebhookPayload>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Subscriber webhook JSON payload is invalid.");
            return BadRequest("Invalid JSON payload.");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.CameraId))
        {
            return BadRequest("CameraId is required.");
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            idempotencyKey = BuildFallbackIdempotencyKey(payload);
        }

        var dedupMinutes = Math.Max(1, _options.CurrentValue.DedupMinutes);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (_cache.TryGetValue(idempotencyKey, out _))
            {
                return Accepted();
            }

            _cache.Set(idempotencyKey, true, TimeSpan.FromMinutes(dedupMinutes));
        }

        var faceEvent = FaceEventDto.FromPayload(payload, idempotencyKey ?? string.Empty);
        _buffer.Add(faceEvent);

        _ = _hub.Clients.All.SendAsync("faceEvent", faceEvent, CancellationToken.None)
            .ContinueWith(task =>
            {
                if (task.Exception is not null)
                {
                    _logger.LogDebug(task.Exception, "Realtime broadcast failed for event {EventId}", faceEvent.Id);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

        return Ok(new { success = true });
    }

    private bool IsEnabled()
    {
        return _options.CurrentValue.Enabled;
    }

    private static string BuildFallbackIdempotencyKey(FaceWebhookPayload payload)
    {
        var raw = payload.FeatureBase64 ?? string.Empty;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        var timestamp = payload.EventTimeUtc == default ? DateTimeOffset.UtcNow : payload.EventTimeUtc;
        return $"{payload.CameraId}:{timestamp.ToUnixTimeMilliseconds()}:{hash}";
    }
}
