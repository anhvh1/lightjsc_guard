using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class DashboardRepository
{
    private const string EventsTable = "face_events";
    private const string CamerasTable = "cameras";
    private const string MapPositionsTable = "map_camera_positions";
    private readonly string _connectionString;

    public DashboardRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["Postgres:ConnectionString"]
            ?? string.Empty;
    }

    public async Task<DashboardSummaryRow> GetSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        DateTime activeSinceUtc,
        CancellationToken cancellationToken)
    {
        var sql = $@"
SELECT
    (SELECT COUNT(*) FROM {EventsTable} e WHERE e.""EventTimeUtc"" >= @FromUtc AND e.""EventTimeUtc"" <= @ToUtc) AS total_count,
    (SELECT COUNT(*) FROM {EventsTable} e WHERE e.""EventTimeUtc"" >= @FromUtc AND e.""EventTimeUtc"" <= @ToUtc AND e.""IsKnown"" = true) AS known_count,
    (SELECT COUNT(*) FROM {EventsTable} e WHERE e.""EventTimeUtc"" >= @FromUtc AND e.""EventTimeUtc"" <= @ToUtc AND e.""IsKnown"" = false) AS unknown_count,
    (SELECT COUNT(*) FROM {EventsTable} e WHERE e.""EventTimeUtc"" >= @FromUtc AND e.""EventTimeUtc"" <= @ToUtc AND e.""IsKnown"" = true) AS match_count,
    (SELECT MAX(e.""EventTimeUtc"") FROM {EventsTable} e) AS last_event_time_utc,
    (SELECT COUNT(*) FROM {CamerasTable} c WHERE c.enabled = true) AS enabled_cameras,
    (SELECT COUNT(*) FROM {CamerasTable} c WHERE c.enabled = false) AS disabled_cameras,
    (SELECT COUNT(DISTINCT e.""CameraId"") FROM {EventsTable} e WHERE e.""EventTimeUtc"" >= @ActiveSinceUtc) AS active_cameras;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleAsync<DashboardSummaryRow>(sql, new
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            ActiveSinceUtc = activeSinceUtc
        });
    }

    public async Task<IReadOnlyList<DashboardTimeseriesRow>> GetTimeseriesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int stepSeconds,
        CancellationToken cancellationToken)
    {
        var fromDayStartUtc = fromUtc.Date;
        var sql = $@"
WITH buckets AS (
    SELECT
        to_timestamp(floor(extract(epoch from e.""EventTimeUtc"") / @StepSeconds) * @StepSeconds) AS bucket_start_utc,
        COUNT(*) AS total_count,
        SUM(CASE WHEN e.""IsKnown"" THEN 1 ELSE 0 END) AS known_count,
        SUM(CASE WHEN NOT e.""IsKnown"" THEN 1 ELSE 0 END) AS unknown_count,
        SUM(CASE WHEN e.""IsKnown"" THEN 1 ELSE 0 END) AS match_count
    FROM {EventsTable} e
    WHERE e.""EventTimeUtc"" >= @FromDayStartUtc AND e.""EventTimeUtc"" <= @ToUtc
    GROUP BY bucket_start_utc
)
SELECT
    bucket_start_utc,
    total_count,
    known_count,
    unknown_count,
    match_count,
    SUM(total_count) OVER (PARTITION BY DATE(bucket_start_utc AT TIME ZONE 'UTC') ORDER BY bucket_start_utc) AS total_cumulative,
    SUM(known_count) OVER (PARTITION BY DATE(bucket_start_utc AT TIME ZONE 'UTC') ORDER BY bucket_start_utc) AS known_cumulative,
    SUM(unknown_count) OVER (PARTITION BY DATE(bucket_start_utc AT TIME ZONE 'UTC') ORDER BY bucket_start_utc) AS unknown_cumulative,
    SUM(match_count) OVER (PARTITION BY DATE(bucket_start_utc AT TIME ZONE 'UTC') ORDER BY bucket_start_utc) AS match_cumulative
FROM buckets
WHERE bucket_start_utc >= @FromUtc
ORDER BY bucket_start_utc;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DashboardTimeseriesRow>(sql, new
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            FromDayStartUtc = fromDayStartUtc,
            StepSeconds = Math.Max(1, stepSeconds)
        });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<DashboardTopCameraRow>> GetTopCamerasAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = $@"
SELECT
    e.""CameraId"" AS camera_id,
    c.camera_code AS camera_code,
    COUNT(*) AS total_count
FROM {EventsTable} e
LEFT JOIN {CamerasTable} c ON c.camera_id = e.""CameraId""
WHERE e.""EventTimeUtc"" >= @FromUtc AND e.""EventTimeUtc"" <= @ToUtc
GROUP BY e.""CameraId"", c.camera_code
ORDER BY total_count DESC
LIMIT @Limit;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DashboardTopCameraRow>(sql, new
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Limit = Math.Clamp(limit, 1, 100)
        });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<DashboardCameraHealthRow>> GetCameraHealthAsync(
        DateTime lookbackUtc,
        DateTime activityWindowUtc,
        CancellationToken cancellationToken)
    {
        var sql = $@"
SELECT
    c.camera_id AS camera_id,
    c.camera_code AS camera_code,
    c.ip_address AS ip_address,
    c.enabled AS enabled,
    MAX(e.""EventTimeUtc"") AS last_event_time_utc,
    COUNT(*) FILTER (WHERE e.""EventTimeUtc"" >= @ActivityWindowUtc) AS events_5m
FROM {CamerasTable} c
LEFT JOIN {EventsTable} e ON e.""CameraId"" = c.camera_id AND e.""EventTimeUtc"" >= @LookbackUtc
GROUP BY c.camera_id, c.camera_code, c.ip_address, c.enabled
ORDER BY c.camera_id;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DashboardCameraHealthRow>(sql, new
        {
            LookbackUtc = lookbackUtc,
            ActivityWindowUtc = activityWindowUtc
        });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<DashboardAlertRow>> GetAlertsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        DateTime? cursorBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var cursorClause = cursorBeforeUtc.HasValue ? "AND e.\"EventTimeUtc\" < @CursorBeforeUtc" : string.Empty;
        var sql = $@"
SELECT
    e.""Id"" AS id,
    e.""EventTimeUtc"" AS event_time_utc,
    e.""CameraId"" AS camera_id,
    c.camera_code AS camera_code,
    e.""IsKnown"" AS is_known,
    e.""WatchlistEntryId"" AS watchlist_entry_id,
    e.""PersonId"" AS person_id,
    e.""PersonJson""::text AS person_json,
    e.""Similarity"" AS similarity,
    e.""Score"" AS score
FROM {EventsTable} e
LEFT JOIN {CamerasTable} c ON c.camera_id = e.""CameraId""
WHERE e.""EventTimeUtc"" >= @FromUtc AND e.""EventTimeUtc"" <= @ToUtc
{cursorClause}
ORDER BY e.""EventTimeUtc"" DESC
LIMIT @Limit;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DashboardAlertRow>(sql, new
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            CursorBeforeUtc = cursorBeforeUtc,
            Limit = Math.Clamp(limit, 1, 200)
        });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<DashboardMapHeatRow>> GetHeatPointsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        Guid? mapId,
        double? minLat,
        double? minLon,
        double? maxLat,
        double? maxLon,
        CancellationToken cancellationToken)
    {
        var clauses = new List<string>
        {
            "p.latitude IS NOT NULL",
            "p.longitude IS NOT NULL"
        };

        if (mapId.HasValue)
        {
            clauses.Add("p.map_id = @MapId");
        }

        if (minLat.HasValue && maxLat.HasValue)
        {
            clauses.Add("p.latitude BETWEEN @MinLat AND @MaxLat");
        }

        if (minLon.HasValue && maxLon.HasValue)
        {
            clauses.Add("p.longitude BETWEEN @MinLon AND @MaxLon");
        }

        var whereClause = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        var sql = $@"
SELECT
    p.camera_id AS camera_id,
    p.label AS label,
    p.latitude AS latitude,
    p.longitude AS longitude,
    COUNT(e.""Id"") AS event_count
FROM {MapPositionsTable} p
LEFT JOIN {EventsTable} e
    ON e.""CameraId"" = p.camera_id
    AND e.""EventTimeUtc"" >= @FromUtc
    AND e.""EventTimeUtc"" <= @ToUtc
{whereClause}
GROUP BY p.camera_id, p.label, p.latitude, p.longitude
ORDER BY event_count DESC;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DashboardMapHeatRow>(sql, new
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            MapId = mapId,
            MinLat = minLat,
            MaxLat = maxLat,
            MinLon = minLon,
            MaxLon = maxLon
        });
        return rows.ToList();
    }
}

public sealed class DashboardSummaryRow
{
    public DashboardSummaryRow()
    {
    }

    public long TotalCount { get; set; }
    public long KnownCount { get; set; }
    public long UnknownCount { get; set; }
    public long MatchCount { get; set; }
    public DateTime? LastEventTimeUtc { get; set; }
    public long EnabledCameras { get; set; }
    public long DisabledCameras { get; set; }
    public long ActiveCameras { get; set; }
}

public sealed class DashboardTimeseriesRow
{
    public DashboardTimeseriesRow()
    {
    }

    public DateTime BucketStartUtc { get; set; }
    public long TotalCount { get; set; }
    public long KnownCount { get; set; }
    public long UnknownCount { get; set; }
    public long MatchCount { get; set; }
    public long TotalCumulative { get; set; }
    public long KnownCumulative { get; set; }
    public long UnknownCumulative { get; set; }
    public long MatchCumulative { get; set; }
}

public sealed class DashboardTopCameraRow
{
    public DashboardTopCameraRow()
    {
    }

    public string CameraId { get; set; } = string.Empty;
    public string? CameraCode { get; set; }
    public long TotalCount { get; set; }
}

public sealed class DashboardCameraHealthRow
{
    public DashboardCameraHealthRow()
    {
    }

    public string CameraId { get; set; } = string.Empty;
    public string? CameraCode { get; set; }
    public string? IpAddress { get; set; }
    public bool Enabled { get; set; }
    public DateTime? LastEventTimeUtc { get; set; }
    public long Events5m { get; set; }
}

public sealed class DashboardAlertRow
{
    public DashboardAlertRow()
    {
    }

    public Guid Id { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string? CameraCode { get; set; }
    public bool IsKnown { get; set; }
    public string? WatchlistEntryId { get; set; }
    public string? PersonId { get; set; }
    public string? PersonJson { get; set; }
    public float? Similarity { get; set; }
    public float? Score { get; set; }
}

public sealed class DashboardMapHeatRow
{
    public DashboardMapHeatRow()
    {
    }

    public string CameraId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public long EventCount { get; set; }
}
