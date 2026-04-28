using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
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

namespace LightJSC.Workers.Services;

public sealed class MatchingWorkerService : BackgroundService
{
    private static readonly ConcurrentDictionary<string, long> DropLogTimes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DropLogInterval = TimeSpan.FromSeconds(30);
    private readonly IngestPipeline _pipeline;
    private readonly IFaceEventDeduplicator _deduplicator;
    private readonly IVectorIndex _vectorIndex;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchingOptions _matchingOptions;
    private readonly WebhookOptions _webhookOptions;
    private readonly WorkerOptions _workerOptions;
    private readonly VectorIndexOptions _vectorOptions;
    private readonly ILogger<MatchingWorkerService> _logger;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly TimeSpan _settingsRefreshInterval = TimeSpan.FromSeconds(30);
    private MatchingSettings _matchingSettings;
    private DateTime _lastSettingsRefreshUtc = DateTime.MinValue;
    private const string MatchingSettingsKey = "matching_settings";
    private static readonly JsonSerializerOptions SettingsSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MatchingWorkerService(
        IngestPipeline pipeline,
        IFaceEventDeduplicator deduplicator,
        IVectorIndex vectorIndex,
        IServiceScopeFactory scopeFactory,
        IOptions<MatchingOptions> matchingOptions,
        IOptions<VectorIndexOptions> vectorOptions,
        IOptions<WebhookOptions> webhookOptions,
        IOptions<WorkerOptions> workerOptions,
        ILogger<MatchingWorkerService> logger)
    {
        _pipeline = pipeline;
        _deduplicator = deduplicator;
        _vectorIndex = vectorIndex;
        _scopeFactory = scopeFactory;
        _matchingOptions = matchingOptions.Value;
        _vectorOptions = vectorOptions.Value;
        _webhookOptions = webhookOptions.Value;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _matchingSettings = BuildDefaultMatchingSettings();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Matching worker starting. DegreeOfParallelism={Degree}",
            Math.Max(1, _workerOptions.MatchDegreeOfParallelism));

        var workers = Enumerable.Range(0, Math.Max(1, _workerOptions.MatchDegreeOfParallelism))
            .Select(_ => Task.Run(() => ProcessAsync(stoppingToken), stoppingToken));

        await Task.WhenAll(workers);

        _logger.LogWarning("Matching worker stopped. All processing loops completed.");
    }

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        await foreach (var faceEvent in _pipeline.FaceEvents.Reader.ReadAllAsync(stoppingToken))
        {
            _pipeline.DecrementFaceEventQueue();
            MetricsRegistry.QueueLength.WithLabels("ingest").Set(_pipeline.FaceEventQueueLength);

            try
            {
                var matchingSettings = await GetMatchingSettingsAsync(stoppingToken);
                if (matchingSettings.Score > 0f)
                {
                    if (!faceEvent.Score.HasValue || faceEvent.Score.Value < matchingSettings.Score)
                    {
                        MetricsRegistry.IngestDropped.WithLabels("min_score").Inc();
                        if (LogRateLimiter.ShouldLog(DropLogTimes, $"min_score:{faceEvent.CameraId}", DropLogInterval))
                        {
                            _logger.LogWarning(
                                "Face event dropped by min score. CameraId={CameraId} Score={Score} Threshold={Threshold}",
                                faceEvent.CameraId,
                                faceEvent.Score,
                                matchingSettings.Score);
                        }
                        continue;
                    }
                }

                if (!_deduplicator.ShouldProcess(faceEvent))
                {
                    MetricsRegistry.IngestDropped.WithLabels("dedup").Inc();
                    if (LogRateLimiter.ShouldLog(DropLogTimes, $"dedup:{faceEvent.CameraId}", DropLogInterval))
                    {
                        _logger.LogInformation(
                            "Face event deduplicated. CameraId={CameraId} EventTime={EventTimeUtc}",
                            faceEvent.CameraId,
                            faceEvent.EventTimeUtc);
                    }
                    continue;
                }

                var stopwatch = Stopwatch.StartNew();
                var topK = Math.Max(1, _vectorOptions.TopK);
                var matches = _vectorIndex.SearchTopK(faceEvent.FeatureVector, faceEvent.FeatureVersion, topK);
                stopwatch.Stop();
                MetricsRegistry.MatchLatency.Observe(stopwatch.Elapsed.TotalSeconds);

                var best = matches.FirstOrDefault();
                var threshold = matchingSettings.Similarity;

                var similarity = best?.Similarity;
                var isKnown = similarity.HasValue && similarity.Value >= threshold;

                if (isKnown)
                {
                    _logger.LogInformation("Known face detected Camera={CameraId} Similarity={Similarity}", faceEvent.CameraId, similarity);
                    if (!_webhookOptions.SendKnown)
                    {
                        continue;
                    }
                }

                var decision = new FaceMatchDecision
                {
                    FaceEvent = faceEvent,
                    IsKnown = isKnown,
                    Similarity = similarity,
                    WatchlistEntryId = isKnown ? best?.EntryId : null,
                    PersonId = isKnown ? best?.Entry?.PersonId : null,
                    Person = isKnown ? best?.Entry?.Person : null,
                    Threshold = threshold
                };

                if (!_pipeline.TryEnqueueDecision(decision))
                {
                    MetricsRegistry.IngestDropped.WithLabels("webhook_queue_full").Inc();
                    if (LogRateLimiter.ShouldLog(DropLogTimes, "decision_queue_full", DropLogInterval))
                    {
                        _logger.LogWarning(
                            "Decision queue full; dropping match result. QueueLength={QueueLength}",
                            _pipeline.DecisionQueueLength);
                    }
                }
                else
                {
                    MetricsRegistry.QueueLength.WithLabels("webhook").Set(_pipeline.DecisionQueueLength);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Matching worker failed for CameraId={CameraId} EventTime={EventTimeUtc}.",
                    faceEvent.CameraId,
                    faceEvent.EventTimeUtc);
            }
        }

        _logger.LogWarning("Matching worker loop completed. Face event channel closed.");
    }

    private MatchingSettings BuildDefaultMatchingSettings()
    {
        return new MatchingSettings
        {
            Similarity = _matchingOptions.Similarity,
            Score = _matchingOptions.Score
        };
    }

    private static MatchingSettings NormalizeSettings(MatchingSettings settings, MatchingSettings fallback)
    {
        return new MatchingSettings
        {
            Similarity = NormalizeValue(settings.Similarity, fallback.Similarity),
            Score = NormalizeValue(settings.Score, fallback.Score)
        };
    }

    private static float NormalizeValue(float value, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    private static float NormalizeScoreValue(float value, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
        {
            return fallback;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    private async Task<MatchingSettings> GetMatchingSettingsAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastSettingsRefreshUtc < _settingsRefreshInterval)
        {
            return _matchingSettings;
        }

        await _settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _lastSettingsRefreshUtc < _settingsRefreshInterval)
            {
                return _matchingSettings;
            }

            var settings = BuildDefaultMatchingSettings();
            using var scope = _scopeFactory.CreateScope();
            var runtimeStateRepository = scope.ServiceProvider.GetRequiredService<IRuntimeStateRepository>();
            var state = await runtimeStateRepository.GetAsync(MatchingSettingsKey, cancellationToken);
            if (state is not null && !string.IsNullOrWhiteSpace(state.Value))
            {
                try
                {
                    using var doc = JsonDocument.Parse(state.Value);
                    var hasSimilarity = HasProperty(doc.RootElement, "similarity");
                    var hasScore = HasProperty(doc.RootElement, "score");
                    var parsed = JsonSerializer.Deserialize<MatchingSettings>(state.Value, SettingsSerializerOptions);
                    if (parsed is not null)
                    {
                        if (hasSimilarity)
                        {
                            settings.Similarity = NormalizeValue(parsed.Similarity, settings.Similarity);
                        }

                        if (hasScore)
                        {
                            settings.Score = NormalizeScoreValue(parsed.Score, settings.Score);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Matching settings JSON is invalid.");
                }
            }

            _matchingSettings = settings;
            _lastSettingsRefreshUtc = DateTime.UtcNow;
            return _matchingSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private static bool HasProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}







