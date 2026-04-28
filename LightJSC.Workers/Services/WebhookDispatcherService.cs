using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Workers.Helpers;
using LightJSC.Workers.Metrics;
using LightJSC.Workers.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace LightJSC.Workers.Services;

public sealed class WebhookDispatcherService : BackgroundService
{
    private static readonly ConcurrentDictionary<string, long> DropLogTimes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DropLogInterval = TimeSpan.FromSeconds(30);
    private readonly IngestPipeline _pipeline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRealtimeEventPublisher _realtimePublisher;
    private readonly WebhookOptions _webhookOptions;
    private readonly WorkerOptions _workerOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BestshotStorageService _bestshotStorage;
    private readonly ILogger<WebhookDispatcherService> _logger;
    private readonly ConcurrentDictionary<Guid, IAsyncPolicy<HttpResponseMessage>> _policies = new();
    private readonly ConcurrentDictionary<string, long> _cooldownMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly Channel<WebhookDispatchJob> _dispatchQueue;
    private List<Subscriber> _subscribers = new();
    private AlarmDeliverySettings _alarmSettings = AlarmDeliverySettings.Default;
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private DateTime _lastNoSubscriberLogUtc = DateTime.MinValue;
    private DateTime _lastSettingsRefreshUtc = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);
    private const int DispatchQueueCapacity = 2048;
    private const string AlarmSettingsKey = "alarm_delivery_settings";
    private static readonly JsonSerializerOptions AlarmSettingsSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WebhookDispatcherService(
        IngestPipeline pipeline,
        IServiceScopeFactory scopeFactory,
        IRealtimeEventPublisher realtimePublisher,
        IOptions<WebhookOptions> webhookOptions,
        IOptions<WorkerOptions> workerOptions,
        IHttpClientFactory httpClientFactory,
        BestshotStorageService bestshotStorage,
        ILogger<WebhookDispatcherService> logger)
    {
        _pipeline = pipeline;
        _scopeFactory = scopeFactory;
        _realtimePublisher = realtimePublisher;
        _webhookOptions = webhookOptions.Value;
        _workerOptions = workerOptions.Value;
        _httpClientFactory = httpClientFactory;
        _bestshotStorage = bestshotStorage;
        _logger = logger;
        _dispatchQueue = Channel.CreateBounded<WebhookDispatchJob>(new BoundedChannelOptions(DispatchQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, _workerOptions.WebhookDegreeOfParallelism);
        _logger.LogInformation(
            "Webhook dispatcher starting. DegreeOfParallelism={Degree} QueueCapacity={Capacity}",
            workerCount,
            DispatchQueueCapacity);
        var decisionWorkers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() => ProcessAsync(stoppingToken), stoppingToken));
        var dispatchWorkers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(() => DispatchLoopAsync(stoppingToken), stoppingToken));

        await Task.WhenAll(decisionWorkers.Concat(dispatchWorkers));

        _logger.LogWarning("Webhook dispatcher stopped. All processing loops completed.");
    }

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        await foreach (var decision in _pipeline.Decisions.Reader.ReadAllAsync(stoppingToken))
        {
            _pipeline.DecrementDecisionQueue();
            MetricsRegistry.QueueLength.WithLabels("webhook").Set(_pipeline.DecisionQueueLength);

            try
            {
                await _bestshotStorage.StoreAsync(decision, stoppingToken);

                if (!ShouldDispatch(decision))
                {
                    continue;
                }

                if (decision.IsKnown && !_webhookOptions.SendKnown)
                {
                    continue;
                }

                var alarmSettings = await GetAlarmSettingsAsync(stoppingToken);
                var alarmType = ResolveAlarmType(decision);
                if (!IsAlarmEnabled(alarmSettings, alarmType))
                {
                    continue;
                }

                _realtimePublisher.TryPublish(decision);

                var subscribers = await GetSubscribersAsync(stoppingToken);
                if (subscribers.Count == 0)
                {
                    if (DateTime.UtcNow - _lastNoSubscriberLogUtc > _refreshInterval)
                    {
                        _logger.LogWarning("No webhook subscribers configured; face events will not be delivered.");
                        _lastNoSubscriberLogUtc = DateTime.UtcNow;
                    }

                    continue;
                }

                var payload = BuildPayload(decision);
                var body = JsonSerializer.Serialize(payload);
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var idempotencyKey = BuildIdempotencyKey(decision.FaceEvent);

                foreach (var subscriber in subscribers)
                {
                    var job = new WebhookDispatchJob(subscriber, body, bodyBytes, idempotencyKey);
                    if (!_dispatchQueue.Writer.TryWrite(job))
                    {
                        if (LogRateLimiter.ShouldLog(DropLogTimes, "dispatch_queue_full", DropLogInterval))
                        {
                            _logger.LogWarning(
                                "Webhook dispatch queue full; dropping subscribers.");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook dispatch loop failed.");
            }
        }

        _logger.LogWarning("Webhook decision loop completed. Decision channel closed.");
    }

    private async Task DispatchLoopAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _dispatchQueue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(job.Subscriber, job.Body, job.BodyBytes, job.IdempotencyKey, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook dispatch worker failed.");
            }
        }

        _logger.LogWarning("Webhook dispatch worker completed. Dispatch queue closed.");
    }

    private async Task DispatchAsync(Subscriber subscriber, string body, byte[] bodyBytes, string idempotencyKey, CancellationToken cancellationToken)
    {
        var policy = _policies.GetOrAdd(subscriber.Id, _ => CreatePolicy());
        var client = _httpClientFactory.CreateClient("webhook");

        try
        {
            var response = await policy.ExecuteAsync(async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, subscriber.EndpointUrl);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.Add("Idempotency-Key", idempotencyKey);
                request.Headers.Add("X-Signature", ComputeSignature(bodyBytes));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                return await client.SendAsync(request, ct);
            }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                MetricsRegistry.WebhookSuccess.WithLabels(subscriber.Id.ToString()).Inc();
                return;
            }

            throw new HttpRequestException($"Webhook failed with status {response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            MetricsRegistry.WebhookFail.WithLabels(subscriber.Id.ToString()).Inc();
            using var scope = _scopeFactory.CreateScope();
            var dlqRepository = scope.ServiceProvider.GetRequiredService<IDlqRepository>();
            await dlqRepository.AddAsync(new DlqMessage
            {
                Id = Guid.NewGuid(),
                SubscriberId = subscriber.Id,
                EndpointUrl = subscriber.EndpointUrl,
                IdempotencyKey = idempotencyKey,
                PayloadJson = body,
                Error = ex.Message,
                AttemptCount = _webhookOptions.MaxRetries + 1,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogWarning(ex, "Webhook dispatch failed for {SubscriberId}", subscriber.Id);
        }
    }

    private IAsyncPolicy<HttpResponseMessage> CreatePolicy()
    {
        var timeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(_webhookOptions.TimeoutSeconds));
        var retry = Policy<HttpResponseMessage>
            .Handle<Exception>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(_webhookOptions.MaxRetries, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        var breaker = Policy<HttpResponseMessage>
            .Handle<Exception>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .CircuitBreakerAsync(_webhookOptions.CircuitBreakerFailures, TimeSpan.FromSeconds(_webhookOptions.CircuitBreakerBreakSeconds));

        // Order matters: apply per-attempt timeout, then retry, then breaker over the whole call.
        return Policy.WrapAsync(breaker, retry, timeout);
    }

    private async Task<List<Subscriber>> GetSubscribersAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastRefreshUtc < _refreshInterval)
        {
            return _subscribers;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _lastRefreshUtc < _refreshInterval)
            {
                return _subscribers;
            }

            using var scope = _scopeFactory.CreateScope();
            var subscriberRepository = scope.ServiceProvider.GetRequiredService<ISubscriberRepository>();
            var list = await subscriberRepository.ListAsync(cancellationToken);
            _subscribers = list.Where(x => x.Enabled).ToList();
            _lastRefreshUtc = DateTime.UtcNow;
            return _subscribers;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private string ComputeSignature(byte[] body)
    {
        if (string.IsNullOrWhiteSpace(_webhookOptions.HmacSecret))
        {
            return string.Empty;
        }

        var key = Encoding.UTF8.GetBytes(_webhookOptions.HmacSecret);
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(body));
    }

    private static string BuildIdempotencyKey(FaceEvent faceEvent)
    {
        var hash = Convert.ToHexString(SHA256.HashData(faceEvent.FeatureBytes));
        return string.Concat(faceEvent.CameraId, ":", faceEvent.EventTimeUtc.ToUnixTimeMilliseconds().ToString(), ":", hash);
    }

    private static WebhookPayload BuildPayload(FaceMatchDecision decision)
    {
        var face = decision.FaceEvent;
        return new WebhookPayload
        {
            CameraId = face.CameraId,
            CameraIp = face.CameraIp,
            CameraCode = face.CameraCode,
            CameraName = face.CameraName,
            EventTimeUtc = face.EventTimeUtc,
            FeatureBase64 = Convert.ToBase64String(face.FeatureBytes),
            L2Norm = face.L2Norm,
            FeatureVersion = face.FeatureVersion,
            Age = face.Age,
            Gender = face.Gender,
            Mask = face.Mask,
            FaceImageBase64 = face.FaceImageBase64,
            BsFrame = face.BsFrame,
            ThumbFrame = face.ThumbFrame,
            Score = face.Score,
            BBox = face.BBox,
            IsKnown = decision.IsKnown,
            Similarity = decision.Similarity,
            WatchlistEntryId = decision.WatchlistEntryId,
            PersonId = decision.PersonId,
            Person = decision.Person
        };
    }

    private sealed class WebhookPayload
    {
        public string CameraId { get; init; } = string.Empty;
        public string? CameraIp { get; init; }
        public string? CameraCode { get; init; }
        public string? CameraName { get; init; }
        public DateTimeOffset EventTimeUtc { get; init; }
        public string FeatureBase64 { get; init; } = string.Empty;
        public float L2Norm { get; init; }
        public string FeatureVersion { get; init; } = string.Empty;
        public int? Age { get; init; }
        public string? Gender { get; init; }
        public string? Mask { get; init; }
        public string? FaceImageBase64 { get; init; }
        public string? BsFrame { get; init; }
        public string? ThumbFrame { get; init; }
        public float? Score { get; init; }
        public BoundingBox? BBox { get; init; }
        public bool IsKnown { get; init; }
        public float? Similarity { get; init; }
        public string? WatchlistEntryId { get; init; }
        public string? PersonId { get; init; }
        public PersonProfile? Person { get; init; }
    }

    private enum AlarmSubjectType
    {
        WhiteList,
        BlackList,
        Protect,
        Undefined
    }

    private bool ShouldDispatch(FaceMatchDecision decision)
    {
        if (_webhookOptions.FaceCooldownSeconds <= 0)
        {
            return true;
        }

        var key = BuildCooldownKey(decision);
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var cooldownTicks = TimeSpan.FromSeconds(_webhookOptions.FaceCooldownSeconds).Ticks;
        if (_cooldownMap.TryGetValue(key, out var lastTicks))
        {
            if (nowTicks - lastTicks < cooldownTicks)
            {
                return false;
            }
        }

        _cooldownMap[key] = nowTicks;
        PruneCooldowns(nowTicks, cooldownTicks);
        return true;
    }

    private string BuildCooldownKey(FaceMatchDecision decision)
    {
        if (decision.IsKnown && !string.IsNullOrWhiteSpace(decision.WatchlistEntryId))
        {
            return $"known:{decision.FaceEvent.CameraId}:{decision.WatchlistEntryId}";
        }

        if (decision.FaceEvent.FeatureBytes.Length == 0)
        {
            return string.Empty;
        }

        var hash = Convert.ToHexString(SHA256.HashData(decision.FaceEvent.FeatureBytes));
        return $"unknown:{decision.FaceEvent.CameraId}:{hash}";
    }

    private void PruneCooldowns(long nowTicks, long cooldownTicks)
    {
        if (_cooldownMap.Count < 500)
        {
            return;
        }

        var cutoff = nowTicks - Math.Max(cooldownTicks * 2, TimeSpan.FromMinutes(5).Ticks);
        foreach (var entry in _cooldownMap)
        {
            if (entry.Value < cutoff)
            {
                _cooldownMap.TryRemove(entry.Key, out _);
            }
        }
    }

    private static AlarmSubjectType ResolveAlarmType(FaceMatchDecision decision)
    {
        if (!decision.IsKnown)
        {
            return AlarmSubjectType.Undefined;
        }

        var listType = decision.Person?.ListType;
        if (string.Equals(listType, PersonListTypes.WhiteList, StringComparison.OrdinalIgnoreCase))
        {
            return AlarmSubjectType.WhiteList;
        }

        if (string.Equals(listType, PersonListTypes.BlackList, StringComparison.OrdinalIgnoreCase))
        {
            return AlarmSubjectType.BlackList;
        }

        return AlarmSubjectType.Protect;
    }

    private static bool IsAlarmEnabled(AlarmDeliverySettings settings, AlarmSubjectType alarmType)
    {
        return alarmType switch
        {
            AlarmSubjectType.WhiteList => settings.SendWhiteList,
            AlarmSubjectType.BlackList => settings.SendBlackList,
            AlarmSubjectType.Protect => settings.SendProtect,
            AlarmSubjectType.Undefined => settings.SendUndefined,
            _ => true
        };
    }

    private async Task<AlarmDeliverySettings> GetAlarmSettingsAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastSettingsRefreshUtc < _refreshInterval)
        {
            return _alarmSettings;
        }

        await _settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _lastSettingsRefreshUtc < _refreshInterval)
            {
                return _alarmSettings;
            }

            using var scope = _scopeFactory.CreateScope();
            var runtimeStateRepository = scope.ServiceProvider.GetRequiredService<IRuntimeStateRepository>();
            var state = await runtimeStateRepository.GetAsync(AlarmSettingsKey, cancellationToken);
            var settings = AlarmDeliverySettings.Default;

            if (state is not null && !string.IsNullOrWhiteSpace(state.Value))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<AlarmDeliverySettings>(
                        state.Value,
                        AlarmSettingsSerializerOptions);
                    if (parsed is not null)
                    {
                        settings = parsed;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Alarm settings JSON is invalid.");
                }
            }

            _alarmSettings = settings;
            _lastSettingsRefreshUtc = DateTime.UtcNow;
            return _alarmSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private sealed record WebhookDispatchJob(
        Subscriber Subscriber,
        string Body,
        byte[] BodyBytes,
        string IdempotencyKey);
}

