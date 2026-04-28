using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class AttendanceRepository
{
    private const string EventsTable = "face_events";
    private const string TemplatesTable = "face_templates";
    private const string PersonsTable = "persons";
    private const string TimeZoneName = "Asia/Bangkok";
    private readonly string _connectionString;

    public AttendanceRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["Postgres:ConnectionString"]
            ?? string.Empty;
    }

    public async Task<AttendanceSummaryData> GetMonthlySummaryAsync(
        AttendanceSummaryFilter filter,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc) = ResolveMonthRange(filter.Year, filter.Month);
        var parameters = new DynamicParameters();
        parameters.Add("FromUtc", fromUtc);
        parameters.Add("ToUtc", toUtc);
        parameters.Add("TimeZoneName", TimeZoneName);

        var whereClause = BuildWhereClause(filter, parameters);
        var sql = $@"
WITH resolved_events AS (
    SELECT
        e.""Id"" AS event_id,
        e.""EventTimeUtc"" AS event_time_utc,
        e.""CameraId"" AS camera_id,
        e.""IsKnown"" AS is_known,
        e.""WatchlistEntryId"" AS watchlist_entry_id,
        e.""PersonId"" AS raw_person_id,
        COALESCE(
            ft.""PersonId"",
            CASE
                WHEN NULLIF(e.""PersonId"", '') ~* '^[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}$'
                    THEN e.""PersonId""::uuid
                ELSE NULL
            END
        ) AS resolved_person_id
    FROM {EventsTable} e
    LEFT JOIN {TemplatesTable} ft ON ft.""Id""::text = e.""WatchlistEntryId""
    {whereClause}
),
ranked AS (
    SELECT
        p.""Id"" AS person_id,
        p.""FirstName"" AS first_name,
        p.""LastName"" AS last_name,
        COALESCE(NULLIF(p.""PersonalId"", ''), p.""Code"") AS personal_id,
        p.""Category"" AS category,
        EXTRACT(DAY FROM timezone(@TimeZoneName, re.event_time_utc))::int AS day_number,
        re.event_id AS event_id,
        re.event_time_utc AS event_time_utc,
        ROW_NUMBER() OVER (
            PARTITION BY p.""Id"", DATE(timezone(@TimeZoneName, re.event_time_utc))
            ORDER BY re.event_time_utc ASC, re.event_id ASC
        ) AS in_rank,
        ROW_NUMBER() OVER (
            PARTITION BY p.""Id"", DATE(timezone(@TimeZoneName, re.event_time_utc))
            ORDER BY re.event_time_utc DESC, re.event_id DESC
        ) AS out_rank
    FROM resolved_events re
    INNER JOIN {PersonsTable} p ON p.""Id"" = re.resolved_person_id
)
SELECT
    person_id AS PersonId,
    first_name AS FirstName,
    last_name AS LastName,
    personal_id AS PersonalId,
    category AS Category,
    day_number AS DayNumber,
    MAX(CASE WHEN in_rank = 1 THEN event_id::text END)::uuid AS InEventId,
    MAX(CASE WHEN in_rank = 1 THEN event_time_utc END) AS InEventTimeUtc,
    MAX(CASE WHEN out_rank = 1 THEN event_id::text END)::uuid AS OutEventId,
    MAX(CASE WHEN out_rank = 1 THEN event_time_utc END) AS OutEventTimeUtc
FROM ranked
GROUP BY person_id, first_name, last_name, personal_id, category, day_number
ORDER BY first_name ASC, last_name ASC, person_id ASC, day_number ASC;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<AttendanceSummaryRow>(sql, parameters)).ToList();
        return new AttendanceSummaryData
        {
            Year = filter.Year,
            Month = filter.Month,
            DaysInMonth = DateTime.DaysInMonth(filter.Year, filter.Month),
            Rows = rows
        };
    }

    private static string BuildWhereClause(AttendanceSummaryFilter filter, DynamicParameters parameters)
    {
        var clauses = new List<string>
        {
            "e.\"IsKnown\" = TRUE",
            "e.\"EventTimeUtc\" >= @FromUtc",
            "e.\"EventTimeUtc\" < @ToUtc"
        };

        if (filter.CameraIds is { Count: > 0 })
        {
            var ids = filter.CameraIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            if (ids.Length > 0)
            {
                clauses.Add("e.\"CameraId\" = ANY(@CameraIds)");
                parameters.Add("CameraIds", ids);
            }
        }

        if (filter.Categories is { Count: > 0 })
        {
            var categories = filter.Categories
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (categories.Length > 0)
            {
                clauses.Add("lower(COALESCE(p.\"Category\", '')) = ANY(@Categories)");
                parameters.Add("Categories", categories);
            }
        }

        if (filter.PersonIds is { Count: > 0 })
        {
            var personIds = filter.PersonIds.Distinct().ToArray();
            if (personIds.Length > 0)
            {
                clauses.Add("p.\"Id\" = ANY(@PersonIds)");
                parameters.Add("PersonIds", personIds);
            }
        }

        return "WHERE " + string.Join(" AND ", clauses);
    }

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ResolveMonthRange(int year, int month)
    {
        var timeZone = ResolveTimeZone();
        var localStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddMonths(1);
        return (
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone))
        );
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneName);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }
}

public sealed class AttendanceSummaryFilter
{
    public int Year { get; init; }
    public int Month { get; init; }
    public IReadOnlyCollection<string>? CameraIds { get; init; }
    public IReadOnlyCollection<string>? Categories { get; init; }
    public IReadOnlyCollection<Guid>? PersonIds { get; init; }
}

public sealed class AttendanceSummaryData
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int DaysInMonth { get; init; }
    public IReadOnlyList<AttendanceSummaryRow> Rows { get; init; } = Array.Empty<AttendanceSummaryRow>();
}

public sealed class AttendanceSummaryRow
{
    public Guid PersonId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PersonalId { get; set; }
    public string? Category { get; set; }
    public int DayNumber { get; set; }
    public Guid? InEventId { get; set; }
    public DateTime? InEventTimeUtc { get; set; }
    public Guid? OutEventId { get; set; }
    public DateTime? OutEventTimeUtc { get; set; }
}
