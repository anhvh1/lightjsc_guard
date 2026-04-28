using LightJSC.Core.Options;
using LightJSC.Workers.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightJSC.Workers.Services;

public sealed class PipelineWatchdogService : BackgroundService
{
    private readonly IngestPipeline _pipeline;
    private readonly PipelineWatchdogOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PipelineWatchdogService> _logger;
    private DateTime _lastQueueLogUtc = DateTime.MinValue;
    private int _faceStallCount;
    private int _decisionStallCount;
    private bool _stopRequested;

    public PipelineWatchdogService(
        IngestPipeline pipeline,
        IOptions<PipelineWatchdogOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<PipelineWatchdogService> logger)
    {
        _pipeline = pipeline;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var nowUtc = DateTime.UtcNow;
            if (_options.QueueLogIntervalSeconds > 0 &&
                nowUtc - _lastQueueLogUtc >= TimeSpan.FromSeconds(_options.QueueLogIntervalSeconds))
            {
                LogQueueState(nowUtc);
                _lastQueueLogUtc = nowUtc;
            }

            EvaluateStall(nowUtc);
        }
    }

    private void LogQueueState(DateTime nowUtc)
    {
        _logger.LogInformation(
            "Pipeline state. FaceQueue={FaceQueue} DecisionQueue={DecisionQueue} LastMetadata={LastMetadata} LastFaceEnqueue={LastFaceEnqueue} LastFaceDequeue={LastFaceDequeue} LastDecisionEnqueue={LastDecisionEnqueue} LastDecisionDequeue={LastDecisionDequeue}",
            _pipeline.FaceEventQueueLength,
            _pipeline.DecisionQueueLength,
            _pipeline.LastMetadataReceivedUtc,
            _pipeline.LastFaceEventEnqueuedUtc,
            _pipeline.LastFaceEventDequeuedUtc,
            _pipeline.LastDecisionEnqueuedUtc,
            _pipeline.LastDecisionDequeuedUtc);

        var lastMetadata = _pipeline.LastMetadataReceivedUtc;
        var lastEnqueue = _pipeline.LastFaceEventEnqueuedUtc;
        if (lastMetadata.HasValue && (!lastEnqueue.HasValue || nowUtc - lastEnqueue.Value.UtcDateTime > TimeSpan.FromSeconds(_options.StallTimeoutSeconds)))
        {
            _logger.LogWarning(
                "Metadata still arriving but no face events enqueued recently. LastMetadata={LastMetadata} LastFaceEnqueue={LastFaceEnqueue}",
                lastMetadata,
                lastEnqueue);
        }
    }

    private void EvaluateStall(DateTime nowUtc)
    {
        var stallTimeout = TimeSpan.FromSeconds(_options.StallTimeoutSeconds);
        var grace = TimeSpan.FromSeconds(_options.StallGraceSeconds);

        var faceStalled = IsQueueStalled(
            _pipeline.FaceEventQueueLength,
            _pipeline.LastFaceEventEnqueuedUtc,
            _pipeline.LastFaceEventDequeuedUtc,
            nowUtc,
            stallTimeout,
            grace);

        _faceStallCount = faceStalled ? _faceStallCount + 1 : 0;
        if (faceStalled)
        {
            _logger.LogWarning(
                "Face event pipeline stall detected. Count={Count} QueueLength={QueueLength} LastEnqueue={LastEnqueue} LastDequeue={LastDequeue}",
                _faceStallCount,
                _pipeline.FaceEventQueueLength,
                _pipeline.LastFaceEventEnqueuedUtc,
                _pipeline.LastFaceEventDequeuedUtc);
        }

        var decisionStalled = IsQueueStalled(
            _pipeline.DecisionQueueLength,
            _pipeline.LastDecisionEnqueuedUtc,
            _pipeline.LastDecisionDequeuedUtc,
            nowUtc,
            stallTimeout,
            grace);

        _decisionStallCount = decisionStalled ? _decisionStallCount + 1 : 0;
        if (decisionStalled)
        {
            _logger.LogWarning(
                "Decision pipeline stall detected. Count={Count} QueueLength={QueueLength} LastEnqueue={LastEnqueue} LastDequeue={LastDequeue}",
                _decisionStallCount,
                _pipeline.DecisionQueueLength,
                _pipeline.LastDecisionEnqueuedUtc,
                _pipeline.LastDecisionDequeuedUtc);
        }

        if (!_options.AutoRestartOnStall || _stopRequested)
        {
            return;
        }

        if (_faceStallCount >= _options.MaxStallCount || _decisionStallCount >= _options.MaxStallCount)
        {
            _stopRequested = true;
            _logger.LogError(
                "Pipeline stalled for {TimeoutSeconds}s; stopping application to allow restart. FaceStallCount={FaceStallCount} DecisionStallCount={DecisionStallCount}",
                _options.StallTimeoutSeconds,
                _faceStallCount,
                _decisionStallCount);
            _lifetime.StopApplication();
        }
    }

    private static bool IsQueueStalled(
        long queueLength,
        DateTimeOffset? lastEnqueueUtc,
        DateTimeOffset? lastDequeueUtc,
        DateTime nowUtc,
        TimeSpan stallTimeout,
        TimeSpan grace)
    {
        if (queueLength <= 0)
        {
            return false;
        }

        if (!lastEnqueueUtc.HasValue)
        {
            return false;
        }

        if (nowUtc - lastEnqueueUtc.Value.UtcDateTime < grace)
        {
            return false;
        }

        if (!lastDequeueUtc.HasValue)
        {
            return true;
        }

        return nowUtc - lastDequeueUtc.Value.UtcDateTime > stallTimeout;
    }
}
