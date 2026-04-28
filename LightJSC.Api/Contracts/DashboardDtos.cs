namespace LightJSC.Api.Contracts;

/// <summary>
/// Dashboard KPI summary response.
/// </summary>
public sealed class DashboardSummaryResponse
{
    /// <summary>Inclusive window start (UTC).</summary>
    public DateTime FromUtc { get; set; }
    /// <summary>Inclusive window end (UTC).</summary>
    public DateTime ToUtc { get; set; }
    /// <summary>Total events in window.</summary>
    public long TotalEvents { get; set; }
    /// <summary>Total known events in window.</summary>
    public long KnownCount { get; set; }
    /// <summary>Total unknown events in window.</summary>
    public long UnknownCount { get; set; }
    /// <summary>Total watchlist matches in window.</summary>
    public long MatchCount { get; set; }
    /// <summary>Enabled cameras.</summary>
    public long EnabledCameras { get; set; }
    /// <summary>Disabled cameras.</summary>
    public long DisabledCameras { get; set; }
    /// <summary>Cameras with recent events.</summary>
    public long ActiveCameras { get; set; }
    /// <summary>Most recent event timestamp (UTC).</summary>
    public DateTime? LastEventUtc { get; set; }
    /// <summary>Snapshot timestamp (UTC).</summary>
    public DateTime GeneratedAtUtc { get; set; }
}

/// <summary>
/// Timeseries point for dashboard charts.
/// </summary>
public sealed class DashboardTimeseriesPoint
{
    /// <summary>Bucket start (UTC).</summary>
    public DateTime BucketStartUtc { get; set; }
    /// <summary>Total events for bucket.</summary>
    public long TotalCount { get; set; }
    /// <summary>Known events for bucket.</summary>
    public long KnownCount { get; set; }
    /// <summary>Unknown events for bucket.</summary>
    public long UnknownCount { get; set; }
    /// <summary>Watchlist match events for bucket.</summary>
    public long MatchCount { get; set; }
    /// <summary>Total events since start of day.</summary>
    public long TotalCumulative { get; set; }
    /// <summary>Known events since start of day.</summary>
    public long KnownCumulative { get; set; }
    /// <summary>Unknown events since start of day.</summary>
    public long UnknownCumulative { get; set; }
    /// <summary>Watchlist match events since start of day.</summary>
    public long MatchCumulative { get; set; }
}

/// <summary>
/// Timeseries response payload.
/// </summary>
public sealed class DashboardTimeseriesResponse
{
    /// <summary>Inclusive window start (UTC).</summary>
    public DateTime FromUtc { get; set; }
    /// <summary>Inclusive window end (UTC).</summary>
    public DateTime ToUtc { get; set; }
    /// <summary>Bucket size in seconds.</summary>
    public int StepSeconds { get; set; }
    /// <summary>Series points.</summary>
    public List<DashboardTimeseriesPoint> Points { get; set; } = new();
}

/// <summary>
/// Top entities response.
/// </summary>
public sealed class DashboardTopResponse
{
    /// <summary>Top items by count.</summary>
    public List<DashboardTopItem> Items { get; set; } = new();
}

/// <summary>
/// Top item payload.
/// </summary>
public sealed class DashboardTopItem
{
    /// <summary>Entity identifier (camera id, site id, etc).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Display label.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Count for the time range.</summary>
    public long Count { get; set; }
}

/// <summary>
/// Camera health response.
/// </summary>
public sealed class DashboardCameraHealthResponse
{
    /// <summary>Snapshot timestamp (UTC).</summary>
    public DateTime GeneratedAtUtc { get; set; }
    /// <summary>Camera health items.</summary>
    public List<DashboardCameraHealthItem> Items { get; set; } = new();
}

/// <summary>
/// Camera health item.
/// </summary>
public sealed class DashboardCameraHealthItem
{
    /// <summary>Camera identifier.</summary>
    public string CameraId { get; set; } = string.Empty;
    /// <summary>Camera code (optional).</summary>
    public string? CameraCode { get; set; }
    /// <summary>Camera IP.</summary>
    public string? IpAddress { get; set; }
    /// <summary>Camera enabled state.</summary>
    public bool Enabled { get; set; }
    /// <summary>Last event time (UTC).</summary>
    public DateTime? LastEventUtc { get; set; }
    /// <summary>Events in last 5 minutes.</summary>
    public long Events5m { get; set; }
    /// <summary>Health state.</summary>
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// Alerts response payload.
/// </summary>
public sealed class DashboardAlertResponse
{
    /// <summary>Alert list.</summary>
    public List<DashboardAlertItem> Items { get; set; } = new();
}

/// <summary>
/// Alert item payload.
/// </summary>
public sealed class DashboardAlertItem
{
    /// <summary>Face event identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Event timestamp (UTC).</summary>
    public DateTime EventTimeUtc { get; set; }
    /// <summary>Camera identifier.</summary>
    public string CameraId { get; set; } = string.Empty;
    /// <summary>Camera code (optional).</summary>
    public string? CameraCode { get; set; }
    /// <summary>Known flag.</summary>
    public bool IsKnown { get; set; }
    /// <summary>Watchlist entry id.</summary>
    public string? WatchlistEntryId { get; set; }
    /// <summary>Person id.</summary>
    public string? PersonId { get; set; }
    /// <summary>Person name (optional).</summary>
    public string? PersonName { get; set; }
    /// <summary>List type (white/black/protect).</summary>
    public string? ListType { get; set; }
    /// <summary>Similarity score.</summary>
    public float? Similarity { get; set; }
    /// <summary>Score.</summary>
    public float? Score { get; set; }
    /// <summary>Severity label.</summary>
    public string Severity { get; set; } = "MEDIUM";
}

/// <summary>
/// Map heat response.
/// </summary>
public sealed class DashboardMapHeatResponse
{
    /// <summary>Heatmap points.</summary>
    public List<DashboardMapHeatPoint> Points { get; set; } = new();
}

/// <summary>
/// Heatmap point payload.
/// </summary>
public sealed class DashboardMapHeatPoint
{
    /// <summary>Camera identifier.</summary>
    public string CameraId { get; set; } = string.Empty;
    /// <summary>Camera label (optional).</summary>
    public string? Label { get; set; }
    /// <summary>Latitude.</summary>
    public double Latitude { get; set; }
    /// <summary>Longitude.</summary>
    public double Longitude { get; set; }
    /// <summary>Event count in range.</summary>
    public long Count { get; set; }
}

/// <summary>
/// System metrics response payload.
/// </summary>
public sealed class DashboardSystemMetricsResponse
{
    /// <summary>Ingest queue length.</summary>
    public double? QueueIngest { get; set; }
    /// <summary>Webhook queue length.</summary>
    public double? QueueWebhook { get; set; }
    /// <summary>Total ingest events.</summary>
    public double? IngestTotal { get; set; }
    /// <summary>Total ingest dropped.</summary>
    public double? IngestDroppedTotal { get; set; }
    /// <summary>Ingest dropped by reason.</summary>
    public Dictionary<string, double> IngestDroppedByReason { get; set; } = new();
    /// <summary>Total webhook success.</summary>
    public double? WebhookSuccessTotal { get; set; }
    /// <summary>Total webhook fail.</summary>
    public double? WebhookFailTotal { get; set; }
    /// <summary>Watchlist size.</summary>
    public double? WatchlistSize { get; set; }
    /// <summary>Approximate match latency p95 (seconds).</summary>
    public double? MatchLatencyP95Seconds { get; set; }
    /// <summary>Snapshot timestamp (UTC).</summary>
    public DateTime GeneratedAtUtc { get; set; }
}
