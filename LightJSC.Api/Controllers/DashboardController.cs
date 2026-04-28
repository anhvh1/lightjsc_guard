using System.Globalization;
using System.Text.Json;
using LightJSC.Api.Contracts;
using LightJSC.Core.Models;
using LightJSC.Infrastructure.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private static readonly TimeSpan DefaultSummaryRange = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultAlertRange = TimeSpan.FromHours(1);
    private static readonly TimeSpan CameraActiveWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CameraWarnWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CameraOfflineWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CameraLookbackWindow = TimeSpan.FromDays(1);
    private readonly DashboardRepository _dashboardRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DashboardController> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public DashboardController(
        DashboardRepository dashboardRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<DashboardController> logger)
    {
        _dashboardRepository = dashboardRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc, DefaultSummaryRange);
        var activeSince = to.UtcDateTime - CameraActiveWindow;

        var row = await _dashboardRepository.GetSummaryAsync(
            from.UtcDateTime,
            to.UtcDateTime,
            activeSince,
            cancellationToken);

        return Ok(new DashboardSummaryResponse
        {
            FromUtc = from.UtcDateTime,
            ToUtc = to.UtcDateTime,
            TotalEvents = row.TotalCount,
            KnownCount = row.KnownCount,
            UnknownCount = row.UnknownCount,
            MatchCount = row.MatchCount,
            EnabledCameras = row.EnabledCameras,
            DisabledCameras = row.DisabledCameras,
            ActiveCameras = row.ActiveCameras,
            LastEventUtc = row.LastEventTimeUtc,
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    [HttpGet("timeseries")]
    public async Task<ActionResult<DashboardTimeseriesResponse>> GetTimeseries(
        [FromQuery] string? metric,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int? stepSeconds,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(metric) &&
            !string.Equals(metric, "events", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only metric=events is supported.");
        }

        var (from, to) = ResolveRange(fromUtc, toUtc, DefaultSummaryRange);
        var step = ResolveStepSeconds(stepSeconds, from, to);

        var rows = await _dashboardRepository.GetTimeseriesAsync(
            from.UtcDateTime,
            to.UtcDateTime,
            step,
            cancellationToken);

        return Ok(new DashboardTimeseriesResponse
        {
            FromUtc = from.UtcDateTime,
            ToUtc = to.UtcDateTime,
            StepSeconds = step,
            Points = rows.Select(row => new DashboardTimeseriesPoint
            {
                BucketStartUtc = DateTime.SpecifyKind(row.BucketStartUtc, DateTimeKind.Utc),
                TotalCount = row.TotalCount,
                KnownCount = row.KnownCount,
                UnknownCount = row.UnknownCount,
                MatchCount = row.MatchCount,
                TotalCumulative = row.TotalCumulative,
                KnownCumulative = row.KnownCumulative,
                UnknownCumulative = row.UnknownCumulative,
                MatchCumulative = row.MatchCumulative
            }).ToList()
        });
    }

    [HttpGet("top")]
    public async Task<ActionResult<DashboardTopResponse>> GetTop(
        [FromQuery] string? metric,
        [FromQuery] string? groupBy,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(metric) &&
            !string.Equals(metric, "events", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only metric=events is supported.");
        }

        if (!string.IsNullOrWhiteSpace(groupBy) &&
            !string.Equals(groupBy, "camera", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only groupBy=camera is supported.");
        }

        var (from, to) = ResolveRange(fromUtc, toUtc, DefaultSummaryRange);
        var rows = await _dashboardRepository.GetTopCamerasAsync(
            from.UtcDateTime,
            to.UtcDateTime,
            limit ?? 10,
            cancellationToken);

        return Ok(new DashboardTopResponse
        {
            Items = rows.Select(row => new DashboardTopItem
            {
                Key = row.CameraId,
                Label = string.IsNullOrWhiteSpace(row.CameraCode) ? row.CameraId : row.CameraCode,
                Count = row.TotalCount
            }).ToList()
        });
    }

    [HttpGet("cameras/health")]
    public async Task<ActionResult<DashboardCameraHealthResponse>> GetCameraHealth(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var lookback = now - CameraLookbackWindow;
        var activityWindow = now - CameraActiveWindow;

        var rows = await _dashboardRepository.GetCameraHealthAsync(
            lookback,
            activityWindow,
            cancellationToken);

        return Ok(new DashboardCameraHealthResponse
        {
            GeneratedAtUtc = now,
            Items = rows.Select(row => new DashboardCameraHealthItem
            {
                CameraId = row.CameraId,
                CameraCode = row.CameraCode,
                IpAddress = row.IpAddress,
                Enabled = row.Enabled,
                LastEventUtc = row.LastEventTimeUtc,
                Events5m = row.Events5m,
                State = ResolveCameraState(row, now)
            }).ToList()
        });
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<DashboardAlertResponse>> GetAlerts(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] DateTimeOffset? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc, DefaultAlertRange);
        var rows = await _dashboardRepository.GetAlertsAsync(
            from.UtcDateTime,
            to.UtcDateTime,
            cursor?.UtcDateTime,
            limit ?? 50,
            cancellationToken);

        var items = rows.Select(row =>
        {
            var person = DeserializePerson(row.PersonJson);
            var listType = person?.ListType;
            var name = ResolvePersonName(person, row.PersonId);

            return new DashboardAlertItem
            {
                Id = row.Id,
                EventTimeUtc = row.EventTimeUtc,
                CameraId = row.CameraId,
                CameraCode = row.CameraCode,
                IsKnown = row.IsKnown,
                WatchlistEntryId = row.WatchlistEntryId,
                PersonId = row.PersonId,
                PersonName = name,
                ListType = listType,
                Similarity = row.Similarity,
                Score = row.Score,
                Severity = ResolveSeverity(row.IsKnown, listType)
            };
        }).ToList();

        return Ok(new DashboardAlertResponse { Items = items });
    }

    [HttpGet("map/heat")]
    public async Task<ActionResult<DashboardMapHeatResponse>> GetMapHeat(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] Guid? mapId,
        [FromQuery] string? bbox,
        CancellationToken cancellationToken)
    {
        var (from, to) = ResolveRange(fromUtc, toUtc, DefaultSummaryRange);
        var (minLat, minLon, maxLat, maxLon) = ParseBbox(bbox);

        var rows = await _dashboardRepository.GetHeatPointsAsync(
            from.UtcDateTime,
            to.UtcDateTime,
            mapId,
            minLat,
            minLon,
            maxLat,
            maxLon,
            cancellationToken);

        return Ok(new DashboardMapHeatResponse
        {
            Points = rows.Select(row => new DashboardMapHeatPoint
            {
                CameraId = row.CameraId,
                Label = row.Label,
                Latitude = row.Latitude,
                Longitude = row.Longitude,
                Count = row.EventCount
            }).ToList()
        });
    }

    [HttpGet("system-metrics")]
    public async Task<ActionResult<DashboardSystemMetricsResponse>> GetSystemMetrics(
        CancellationToken cancellationToken)
    {
        var response = new DashboardSystemMetricsResponse
        {
            GeneratedAtUtc = DateTime.UtcNow
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            using var httpResponse = await client.GetAsync($"{baseUrl}/metrics", cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                return Ok(response);
            }

            var text = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            ApplyMetrics(text, response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load system metrics.");
        }

        return Ok(response);
    }

    private static (DateTimeOffset From, DateTimeOffset To) ResolveRange(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        TimeSpan defaultRange)
    {
        var to = toUtc ?? DateTimeOffset.UtcNow;
        var from = fromUtc ?? to.Add(-defaultRange);
        if (from > to)
        {
            (from, to) = (to, from);
        }

        return (from, to);
    }

    private static int ResolveStepSeconds(int? requestedStepSeconds, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var step = requestedStepSeconds.GetValueOrDefault(5);
        step = Math.Clamp(step, 5, 300);

        var rangeSeconds = Math.Max(1, (toUtc - fromUtc).TotalSeconds);
        var maxPoints = 360d;
        if (rangeSeconds / step > maxPoints)
        {
            step = (int)Math.Ceiling(rangeSeconds / maxPoints);
        }

        return Math.Clamp(step, 5, 3600);
    }

    private static string ResolveCameraState(DashboardCameraHealthRow row, DateTime nowUtc)
    {
        if (!row.Enabled)
        {
            return "DISABLED";
        }

        if (!row.LastEventTimeUtc.HasValue)
        {
            return "OFFLINE";
        }

        var age = nowUtc - row.LastEventTimeUtc.Value;
        if (age > CameraOfflineWindow)
        {
            return "OFFLINE";
        }

        if (age > CameraWarnWindow)
        {
            return "WARN";
        }

        return "OK";
    }

    private PersonProfile? DeserializePerson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PersonProfile>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize person payload for dashboard.");
            return null;
        }
    }

    private static string? ResolvePersonName(PersonProfile? person, string? personId)
    {
        if (person is null)
        {
            return personId;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(person.FirstName))
        {
            parts.Add(person.FirstName);
        }

        if (!string.IsNullOrWhiteSpace(person.LastName))
        {
            parts.Add(person.LastName);
        }

        var name = string.Join(" ", parts).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return person.Code is null ? name : $"{name} ({person.Code})";
        }

        return person.Code ?? personId;
    }

    private static string ResolveSeverity(bool isKnown, string? listType)
    {
        if (!isKnown)
        {
            return "MEDIUM";
        }

        if (string.Equals(listType, PersonListTypes.BlackList, StringComparison.OrdinalIgnoreCase))
        {
            return "CRITICAL";
        }

        if (string.Equals(listType, PersonListTypes.WhiteList, StringComparison.OrdinalIgnoreCase))
        {
            return "LOW";
        }

        return "HIGH";
    }

    private static (double? MinLat, double? MinLon, double? MaxLat, double? MaxLon) ParseBbox(string? bbox)
    {
        if (string.IsNullOrWhiteSpace(bbox))
        {
            return (null, null, null, null);
        }

        var parts = bbox.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return (null, null, null, null);
        }

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLat) ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLon) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLat) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLon))
        {
            return (null, null, null, null);
        }

        return (minLat, minLon, maxLat, maxLon);
    }

    private static void ApplyMetrics(string payload, DashboardSystemMetricsResponse response)
    {
        var buckets = new Dictionary<double, double>();
        double? histogramTotal = null;
        var lines = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var metric = parts[0];
            var name = metric;
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var labelStart = metric.IndexOf('{');
            if (labelStart >= 0)
            {
                name = metric[..labelStart];
                var labelEnd = metric.IndexOf('}', labelStart + 1);
                if (labelEnd > labelStart)
                {
                    var labelBody = metric.Substring(labelStart + 1, labelEnd - labelStart - 1);
                    var labelPairs = labelBody.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var pair in labelPairs)
                    {
                        var tokens = pair.Split('=', 2);
                        if (tokens.Length != 2)
                        {
                            continue;
                        }

                        var key = tokens[0].Trim();
                        var val = tokens[1].Trim().Trim('"');
                        labels[key] = val;
                    }
                }
            }

            switch (name)
            {
                case "ipro_queue_length":
                    if (labels.TryGetValue("queue", out var queue))
                    {
                        if (string.Equals(queue, "ingest", StringComparison.OrdinalIgnoreCase))
                        {
                            response.QueueIngest = value;
                        }
                        else if (string.Equals(queue, "webhook", StringComparison.OrdinalIgnoreCase))
                        {
                            response.QueueWebhook = value;
                        }
                    }
                    break;
                case "ipro_ingest_events_total":
                    response.IngestTotal = (response.IngestTotal ?? 0) + value;
                    break;
                case "ipro_ingest_dropped_total":
                    response.IngestDroppedTotal = (response.IngestDroppedTotal ?? 0) + value;
                    if (labels.TryGetValue("reason", out var reason))
                    {
                        response.IngestDroppedByReason[reason] =
                            response.IngestDroppedByReason.TryGetValue(reason, out var existing)
                                ? existing + value
                                : value;
                    }
                    break;
                case "ipro_webhook_success_total":
                    response.WebhookSuccessTotal = (response.WebhookSuccessTotal ?? 0) + value;
                    break;
                case "ipro_webhook_fail_total":
                    response.WebhookFailTotal = (response.WebhookFailTotal ?? 0) + value;
                    break;
                case "ipro_watchlist_size":
                    response.WatchlistSize = value;
                    break;
                case "ipro_match_latency_seconds_bucket":
                    if (!labels.TryGetValue("le", out var le))
                    {
                        break;
                    }

                    if (string.Equals(le, "+Inf", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(le, "Inf", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(le, "Infinity", StringComparison.OrdinalIgnoreCase))
                    {
                        histogramTotal = value;
                        break;
                    }

                    if (double.TryParse(le, NumberStyles.Float, CultureInfo.InvariantCulture, out var bucket))
                    {
                        buckets[bucket] = value;
                    }
                    break;
            }
        }

        if (buckets.Count > 0)
        {
            var ordered = buckets.OrderBy(x => x.Key).ToList();
            var total = histogramTotal ?? ordered.Last().Value;
            if (total > 0)
            {
                foreach (var bucket in ordered)
                {
                    if (bucket.Value / total >= 0.95)
                    {
                        response.MatchLatencyP95Seconds = bucket.Key;
                        break;
                    }
                }
            }
        }
    }
}
