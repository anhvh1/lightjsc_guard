using System.Collections.Concurrent;
using System.Text;
using LightJSC.Core.Helpers;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Workers.Helpers;
using LightJSC.Workers.Metrics;
using LightJSC.Workers.Pipeline;
using Microsoft.Extensions.Logging;

namespace LightJSC.Workers.Services;

public sealed class CameraConnector
{
    private static readonly object MetadataFileLock = new();
    private static readonly ConcurrentDictionary<string, long> DropLogTimes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DropLogInterval = TimeSpan.FromSeconds(30);
    private readonly CameraConnectionInfo _info;
    private readonly IRtspMetadataClient _rtspClient;
    private readonly IFaceMetadataParser _parser;
    private readonly IngestPipeline _pipeline;
    private readonly IngestOptions _ingestOptions;
    private readonly ILogger<CameraConnector> _logger;
    private readonly string _contentRootPath;
    private readonly CameraMetadata _cameraMetadata;
    private readonly object _sync = new();
    private static readonly TimeZoneInfo? HanoiTimeZone = ResolveHanoiTimeZone();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private bool _logInitialized;
    private DateTimeOffset? _lastErrorAt;
    private string? _lastErrorMessage;
    private DateTimeOffset? _lastStoppedAt;

    public CameraConnector(
        CameraConnectionInfo info,
        IRtspMetadataClient rtspClient,
        IFaceMetadataParser parser,
        IngestPipeline pipeline,
        IngestOptions ingestOptions,
        ILogger<CameraConnector> logger,
        string contentRootPath)
    {
        _info = info;
        _rtspClient = rtspClient;
        _parser = parser;
        _pipeline = pipeline;
        _ingestOptions = ingestOptions;
        _logger = logger;
        _contentRootPath = string.IsNullOrWhiteSpace(contentRootPath) ? Directory.GetCurrentDirectory() : contentRootPath;
        _cameraMetadata = BuildCameraMetadata(info);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_runTask is not null && !_runTask.IsCompleted)
            {
                return;
            }

            if (_runTask is not null && _runTask.IsCompleted)
            {
                _logger.LogWarning(
                    "Camera connector restarting for {CameraId}. LastErrorAt={LastErrorAt} LastError={LastError}",
                    _info.CameraId,
                    _lastErrorAt,
                    _lastErrorMessage ?? "none");
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _runTask is not null && !_runTask.IsCompleted;
            }
        }
    }

    public DateTimeOffset? LastErrorAt
    {
        get
        {
            lock (_sync)
            {
                return _lastErrorAt;
            }
        }
    }

    public string? LastErrorMessage
    {
        get
        {
            lock (_sync)
            {
                return _lastErrorMessage;
            }
        }
    }

    public DateTimeOffset? LastStoppedAt
    {
        get
        {
            lock (_sync)
            {
                return _lastStoppedAt;
            }
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? runTask;
        lock (_sync)
        {
            cts = _cts;
            runTask = _runTask;
            _cts = null;
            _runTask = null;
        }

        if (cts is null || runTask is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var uri = _info.ToRtspUri();
        InitializeLogFiles();
        _logger.LogInformation("Camera connector run started for {CameraId}", _info.CameraId);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (var payload in _rtspClient.StreamMetadataAsync(uri, cancellationToken))
                    {
                        var receivedAt = DateTimeOffset.UtcNow;
                        _pipeline.RecordMetadataReceived(receivedAt);
                        if (_ingestOptions.LogRawMetadata)
                        {
                            var maxChars = Math.Max(256, _ingestOptions.MaxRawMetadataChars);
                            var text = payload.Length <= maxChars
                                ? payload
                                : payload.Substring(0, maxChars) + "...(truncated)";
                            _logger.LogInformation(
                                "RTSP raw metadata for {CameraId} ({Length} chars): {Payload}",
                                _info.CameraId,
                                payload.Length,
                                text);
                            WriteRawMetadataToFile(text, payload.Length);
                        }

                        if (!_parser.TryParse(_cameraMetadata, payload, receivedAt, out var faceEvent))
                        {
                            MetricsRegistry.IngestDropped.WithLabels("parse_failed").Inc();
                            if (LogRateLimiter.ShouldLog(DropLogTimes, $"{_info.CameraId}:parse_failed", DropLogInterval))
                            {
                                _logger.LogWarning("Face metadata parse failed. CameraId={CameraId}", _info.CameraId);
                            }
                            continue;
                        }

                        faceEvent.FeatureVersion = FeatureVersionHelper.Combine(
                            faceEvent.FeatureVersion,
                            _info.CameraSeries);

                        var minDimension = Math.Max(0, _ingestOptions.MinFeatureDimension);
                        if (minDimension > 0 && faceEvent.FeatureVector.Length < minDimension)
                        {
                            MetricsRegistry.IngestDropped.WithLabels("min_dimension").Inc();
                            if (LogRateLimiter.ShouldLog(DropLogTimes, $"{_info.CameraId}:min_dimension", DropLogInterval))
                            {
                                _logger.LogWarning(
                                    "Face event dropped due to short feature vector. CameraId={CameraId} FeatureDimension={FeatureDimension} MinDimension={MinDimension}",
                                    faceEvent.CameraId,
                                    faceEvent.FeatureVector.Length,
                                    minDimension);
                            }
                            continue;
                        }

                        if (_ingestOptions.LogParsedEvents)
                        {
                            var maxChars = Math.Max(256, _ingestOptions.MaxRawMetadataChars);
                            var featureBase64 = faceEvent.FeatureBytes.Length == 0
                                ? string.Empty
                                : Convert.ToBase64String(faceEvent.FeatureBytes);
                            if (featureBase64.Length > maxChars)
                            {
                                featureBase64 = featureBase64.Substring(0, maxChars) + "...(truncated)";
                            }

                            _logger.LogInformation(
                                "Face event parsed for {CameraId} at {EventTimeUtc}. FeatureVersion={FeatureVersion} FeatureBytes={FeatureBytesLength} FeatureBase64={FeatureBase64} FaceImageBase64Length={FaceImageBase64Length} L2Norm={L2Norm} Age={Age} Gender={Gender} Mask={Mask} Score={Score} BsFrame={BsFrame} ThumbFrame={ThumbFrame} BBox={BBox}",
                                faceEvent.CameraId,
                                faceEvent.EventTimeUtc,
                                faceEvent.FeatureVersion,
                                faceEvent.FeatureBytes.Length,
                                featureBase64,
                                faceEvent.FaceImageBase64?.Length ?? 0,
                                faceEvent.L2Norm,
                                faceEvent.Age,
                                faceEvent.Gender,
                                faceEvent.Mask,
                                faceEvent.Score,
                                faceEvent.BsFrame,
                                faceEvent.ThumbFrame,
                                faceEvent.BBox);

                            WriteMetadataToFile(payload, faceEvent, featureBase64);
                        }

                        if (_pipeline.TryEnqueueFaceEvent(faceEvent))
                        {
                            MetricsRegistry.IngestEvents.WithLabels(_info.CameraId).Inc();
                            MetricsRegistry.QueueLength.WithLabels("ingest").Set(_pipeline.FaceEventQueueLength);
                        }
                        else
                        {
                            MetricsRegistry.IngestDropped.WithLabels("queue_full").Inc();
                            if (LogRateLimiter.ShouldLog(DropLogTimes, $"{_info.CameraId}:queue_full", DropLogInterval))
                            {
                                _logger.LogWarning(
                                    "Face event queue full. CameraId={CameraId} QueueLength={QueueLength}",
                                    faceEvent.CameraId,
                                    _pipeline.FaceEventQueueLength);
                            }
                        }
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "RTSP metadata stream ended unexpectedly for {CameraId}. Reconnecting.",
                            _info.CameraId);
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (_sync)
                    {
                        _lastErrorAt = DateTimeOffset.UtcNow;
                        _lastErrorMessage = ex.Message;
                    }
                    _logger.LogError(ex, "Camera connector loop failed for {CameraId}", _info.CameraId);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _lastStoppedAt = DateTimeOffset.UtcNow;
            }
        }

        _logger.LogWarning("RTSP metadata stream ended for {CameraId}", _info.CameraId);
    }

    private void WriteMetadataToFile(string rawPayload, FaceEvent faceEvent, string featureBase64)
    {
        if (string.IsNullOrWhiteSpace(_ingestOptions.MetadataLogPath))
        {
            return;
        }

        var logTime = ToHanoiTime(DateTimeOffset.UtcNow);
        var eventTime = ToHanoiTime(faceEvent.EventTimeUtc);
        var maxChars = Math.Max(256, _ingestOptions.MaxRawMetadataChars);
        var raw = rawPayload.Length <= maxChars
            ? rawPayload
            : rawPayload.Substring(0, maxChars) + "...(truncated)";

        var builder = new StringBuilder();
        builder.Append('[').Append(logTime.ToString("o")).Append("] ");
        builder.Append("camera=").Append(faceEvent.CameraId).Append(' ');
        builder.Append("eventTime=").Append(eventTime.ToString("o")).Append(' ');
        builder.Append("featureVersion=").Append(faceEvent.FeatureVersion).Append(' ');
        builder.Append("featureBytes=").Append(faceEvent.FeatureBytes.Length).Append(' ');
        builder.Append("featureBase64=").Append(featureBase64).Append(' ');
        builder.Append("faceImageBase64Length=").Append(faceEvent.FaceImageBase64?.Length ?? 0).Append(' ');
        builder.Append("l2Norm=").Append(faceEvent.L2Norm).Append(' ');
        builder.Append("age=").Append(faceEvent.Age?.ToString() ?? "null").Append(' ');
        builder.Append("gender=").Append(faceEvent.Gender ?? "null").Append(' ');
        builder.Append("mask=").Append(faceEvent.Mask ?? "null").Append(' ');
        builder.Append("score=").Append(faceEvent.Score?.ToString("0.000") ?? "null").Append(' ');
        builder.Append("bsFrame=").Append(faceEvent.BsFrame is null ? "null" : "present").Append(' ');
        builder.Append("thumbFrame=").Append(faceEvent.ThumbFrame is null ? "null" : "present").Append(' ');
        builder.Append("bbox=").Append(faceEvent.BBox?.ToString() ?? "null").Append(' ');
        builder.Append("raw=").Append(raw);

        var path = ResolveLogPath(_ingestOptions.MetadataLogPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (MetadataFileLock)
        {
            File.AppendAllText(path, builder.ToString() + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void WriteRawMetadataToFile(string rawPayload, int length)
    {
        var path = string.IsNullOrWhiteSpace(_ingestOptions.RawMetadataLogPath)
            ? _ingestOptions.MetadataLogPath
            : _ingestOptions.RawMetadataLogPath;
        path = ResolveLogPath(path);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append('[').Append(DateTimeOffset.UtcNow.ToString("o")).Append("] ");
        builder.Append("camera=").Append(_info.CameraId).Append(' ');
        builder.Append("length=").Append(length).Append(' ');
        builder.Append("raw=").Append(rawPayload);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        lock (MetadataFileLock)
        {
            File.AppendAllText(path, builder.ToString() + Environment.NewLine, Encoding.UTF8);
        }
    }

    private void InitializeLogFiles()
    {
        if (_logInitialized)
        {
            return;
        }

        _logInitialized = true;
        var logPaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(_ingestOptions.MetadataLogPath))
        {
            logPaths.Add(_ingestOptions.MetadataLogPath);
        }

        if (_ingestOptions.LogRawMetadata && !string.IsNullOrWhiteSpace(_ingestOptions.RawMetadataLogPath))
        {
            logPaths.Add(_ingestOptions.RawMetadataLogPath);
        }

        logPaths = logPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (logPaths.Count == 0)
        {
            return;
        }

        foreach (var path in logPaths)
        {
            var fullPath = ResolveLogPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"[{ToHanoiTime(DateTimeOffset.UtcNow):o}] camera={_info.CameraId} info=connector-started logPath={fullPath}";
            lock (MetadataFileLock)
            {
                File.AppendAllText(fullPath, line + Environment.NewLine, Encoding.UTF8);
            }

            _logger.LogInformation("Metadata log path resolved for {CameraId}: {LogPath}", _info.CameraId, fullPath);
        }
    }

    private string ResolveLogPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return Path.Combine(_contentRootPath, trimmed);
    }

    private static DateTimeOffset ToHanoiTime(DateTimeOffset utcTime)
    {
        if (HanoiTimeZone is null)
        {
            return utcTime.ToOffset(TimeSpan.FromHours(7));
        }

        return TimeZoneInfo.ConvertTime(utcTime, HanoiTimeZone);
    }

    private static TimeZoneInfo? ResolveHanoiTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        return null;
    }

    private static CameraMetadata BuildCameraMetadata(CameraConnectionInfo info)
    {
        var name = !string.IsNullOrWhiteSpace(info.CameraName)
            ? info.CameraName
            : (!string.IsNullOrWhiteSpace(info.CameraModel)
                ? info.CameraModel
                : info.CameraId);

        return new CameraMetadata
        {
            CameraId = info.CameraId,
            IpAddress = info.IpAddress,
            CameraCode = info.CameraCode,
            CameraName = name
        };
    }

    public sealed class CameraConnectionInfo
    {
        public string CameraId { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
        public string? CameraCode { get; init; }
        public string? CameraName { get; init; }
        public string RtspUsername { get; init; } = string.Empty;
        public string RtspPassword { get; init; } = string.Empty;
        public string RtspProfile { get; init; } = "def_profile1";
        public string RtspPath { get; init; } = "/ONVIF/MediaInput";
        public string CameraSeries { get; init; } = string.Empty;
        public string CameraModel { get; init; } = string.Empty;

        public Uri ToRtspUri()
        {
            var (host, port) = ParseHostPort(IpAddress);
            var builder = new UriBuilder
            {
                Scheme = "rtsp",
                Host = host,
                Port = port,
                UserName = RtspUsername,
                Password = RtspPassword,
                Path = RtspPath.StartsWith("/", StringComparison.Ordinal) ? RtspPath : "/" + RtspPath,
                Query = "profile=" + Uri.EscapeDataString(RtspProfile)
            };

            return builder.Uri;
        }

        private static (string Host, int Port) ParseHostPort(string ipAddress)
        {
            var host = ipAddress;
            var port = 554;

            var parts = ipAddress.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out var parsedPort))
            {
                host = parts[0];
                port = parsedPort;
            }

            return (host, port);
        }
    }
}

