using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class FaceEventSearchRepository
{
    private const string EventsTable = "face_events";
    private const string CamerasTable = "cameras";
    private const string TemplatesTable = "face_templates";
    private readonly string _connectionString;
    private readonly IFaceEventIndex _eventIndex;
    private readonly VectorIndexOptions _vectorOptions;
    private readonly ILogger<FaceEventSearchRepository> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public FaceEventSearchRepository(
        IConfiguration configuration,
        IFaceEventIndex eventIndex,
        IOptions<VectorIndexOptions> vectorOptions,
        ILogger<FaceEventSearchRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["Postgres:ConnectionString"]
            ?? string.Empty;
        _eventIndex = eventIndex;
        _vectorOptions = vectorOptions.Value;
        _logger = logger;
    }

    public async Task<FaceEventSearchResult> SearchAsync(FaceEventSearchFilter filter, CancellationToken cancellationToken)
    {
        var indexTables = await _eventIndex.ListTablesAsync(cancellationToken);
        var includeIndex = indexTables.Count > 0;
        if (!includeIndex && filter.HasFeature == true)
        {
            return new FaceEventSearchResult(Array.Empty<FaceEventSearchRow>(), 0);
        }

        var parameters = new DynamicParameters();
        var hasFeatureExpression = includeIndex
            ? BuildHasFeatureExpression(indexTables, "e.\"Id\"")
            : string.Empty;
        var whereClause = BuildWhereClause(
            filter,
            parameters,
            includeIndex ? hasFeatureExpression : null,
            includeWatchlistIds: true);

        var (limit, offset) = ResolvePaging(filter);
        parameters.Add("Limit", limit);
        parameters.Add("Offset", offset);

        var selectHasFeature = includeIndex ? $"{hasFeatureExpression} AS HasFeature" : "false AS HasFeature";
        var sql = $@"
SELECT
    e.""Id"" AS Id,
    e.""EventTimeUtc"" AS EventTimeUtc,
    e.""CameraId"" AS CameraId,
    c.ip_address AS CameraIp,
    NULL::text AS CameraZone,
    e.""IsKnown"" AS IsKnown,
    e.""WatchlistEntryId"" AS WatchlistEntryId,
    e.""PersonId"" AS PersonId,
    e.""PersonJson""::text AS PersonJson,
    e.""Similarity"" AS WatchlistSimilarity,
    e.""Score"" AS Score,
    e.""BestshotPath"" AS BestshotPath,
    e.""Gender"" AS Gender,
    e.""Age"" AS Age,
    e.""Mask"" AS Mask,
    e.""CreatedAt"" AS CreatedAt,
    {selectHasFeature},
    NULL::real AS TraceSimilarity
FROM {EventsTable} e
LEFT JOIN {CamerasTable} c ON c.camera_id = e.""CameraId""
{whereClause}
ORDER BY e.""EventTimeUtc"" DESC
LIMIT @Limit OFFSET @Offset;";

        var countSql = $@"
SELECT COUNT(*)
FROM {EventsTable} e
LEFT JOIN {CamerasTable} c ON c.camera_id = e.""CameraId""
{whereClause};";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var items = (await connection.QueryAsync<FaceEventSearchRow>(sql, parameters)).ToList();
        var total = await connection.ExecuteScalarAsync<int>(countSql, parameters);
        return new FaceEventSearchResult(items, total);
    }

    public async Task<FaceEventSearchRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var indexTables = await _eventIndex.ListTablesAsync(cancellationToken);
        var includeIndex = indexTables.Count > 0;
        var hasFeatureExpression = includeIndex
            ? BuildHasFeatureExpression(indexTables, "e.\"Id\"")
            : string.Empty;
        var selectHasFeature = includeIndex ? $"{hasFeatureExpression} AS HasFeature" : "false AS HasFeature";

        var sql = $@"
SELECT
    e.""Id"" AS Id,
    e.""EventTimeUtc"" AS EventTimeUtc,
    e.""CameraId"" AS CameraId,
    c.ip_address AS CameraIp,
    NULL::text AS CameraZone,
    e.""IsKnown"" AS IsKnown,
    e.""WatchlistEntryId"" AS WatchlistEntryId,
    e.""PersonId"" AS PersonId,
    e.""PersonJson""::text AS PersonJson,
    e.""Similarity"" AS WatchlistSimilarity,
    e.""Score"" AS Score,
    e.""BestshotPath"" AS BestshotPath,
    e.""Gender"" AS Gender,
    e.""Age"" AS Age,
    e.""Mask"" AS Mask,
    e.""CreatedAt"" AS CreatedAt,
    {selectHasFeature},
    NULL::real AS TraceSimilarity
FROM {EventsTable} e
LEFT JOIN {CamerasTable} c ON c.camera_id = e.""CameraId""
WHERE e.""Id"" = @Id;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<FaceEventSearchRow>(sql, new { Id = id });
    }

    public async Task<FaceEventSearchResult> SearchSimilarAsync(
        float[] vector,
        string? featureVersion,
        int topK,
        FaceEventSearchFilter filter,
        CancellationToken cancellationToken)
    {
        if (vector.Length == 0 || topK <= 0)
        {
            return new FaceEventSearchResult(Array.Empty<FaceEventSearchRow>(), 0);
        }

        var indexTable = await _eventIndex.ResolveTableNameAsync(featureVersion, vector.Length, cancellationToken);
        if (string.IsNullOrWhiteSpace(indexTable))
        {
            return new FaceEventSearchResult(Array.Empty<FaceEventSearchRow>(), 0);
        }

        if (filter.HasFeature == false)
        {
            return new FaceEventSearchResult(Array.Empty<FaceEventSearchRow>(), 0);
        }

        var parameters = new DynamicParameters();
        parameters.Add("FeatureVector", BuildVectorLiteral(vector));
        parameters.Add("Limit", Math.Max(1, topK));

        var whereClause = BuildWhereClause(filter, parameters, hasFeatureExpression: null, includeWatchlistIds: true);
        var quotedIndexTable = QuoteIdentifier(indexTable);
        var sql = $@"
SELECT
    e.""Id"" AS Id,
    e.""EventTimeUtc"" AS EventTimeUtc,
    e.""CameraId"" AS CameraId,
    c.ip_address AS CameraIp,
    NULL::text AS CameraZone,
    e.""IsKnown"" AS IsKnown,
    e.""WatchlistEntryId"" AS WatchlistEntryId,
    e.""PersonId"" AS PersonId,
    e.""PersonJson""::text AS PersonJson,
    e.""Similarity"" AS WatchlistSimilarity,
    e.""Score"" AS Score,
    e.""BestshotPath"" AS BestshotPath,
    e.""Gender"" AS Gender,
    e.""Age"" AS Age,
    e.""Mask"" AS Mask,
    e.""CreatedAt"" AS CreatedAt,
    true AS HasFeature,
    (1 - (i.feature_vector <=> @FeatureVector::vector)) AS TraceSimilarity
FROM {quotedIndexTable} i
JOIN {EventsTable} e ON e.""Id"" = i.event_id
LEFT JOIN {CamerasTable} c ON c.camera_id = e.""CameraId""
{whereClause}
ORDER BY i.feature_vector <=> @FeatureVector::vector
LIMIT @Limit;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        ApplyVectorSearchSettings(connection);

        var items = (await connection.QueryAsync<FaceEventSearchRow>(sql, parameters)).ToList();
        return new FaceEventSearchResult(items, items.Count);
    }

    public async Task<FaceEventSearchResult> SearchSimilarToEventAsync(
        Guid eventId,
        int topK,
        FaceEventSearchFilter filter,
        CancellationToken cancellationToken)
    {
        if (topK <= 0)
        {
            return new FaceEventSearchResult(Array.Empty<FaceEventSearchRow>(), 0);
        }

        var indexTable = await _eventIndex.ResolveTableForEventAsync(eventId, cancellationToken);
        if (string.IsNullOrWhiteSpace(indexTable))
        {
            return new FaceEventSearchResult(Array.Empty<FaceEventSearchRow>(), 0);
        }

        if (filter.HasFeature == false)
        {
            return new FaceEventSearchResult(Array.Empty<FaceEventSearchRow>(), 0);
        }

        var parameters = new DynamicParameters();
        parameters.Add("EventId", eventId);
        parameters.Add("Limit", Math.Max(1, topK));

        var whereClause = BuildWhereClause(filter, parameters, hasFeatureExpression: null, includeWatchlistIds: true);
        if (string.IsNullOrWhiteSpace(whereClause))
        {
            whereClause = "WHERE src.event_id = @EventId";
        }
        else
        {
            whereClause += " AND src.event_id = @EventId";
        }
        var quotedIndexTable = QuoteIdentifier(indexTable);
        var sql = $@"
SELECT
    e.""Id"" AS Id,
    e.""EventTimeUtc"" AS EventTimeUtc,
    e.""CameraId"" AS CameraId,
    c.ip_address AS CameraIp,
    NULL::text AS CameraZone,
    e.""IsKnown"" AS IsKnown,
    e.""WatchlistEntryId"" AS WatchlistEntryId,
    e.""PersonId"" AS PersonId,
    e.""PersonJson""::text AS PersonJson,
    e.""Similarity"" AS WatchlistSimilarity,
    e.""Score"" AS Score,
    e.""BestshotPath"" AS BestshotPath,
    e.""Gender"" AS Gender,
    e.""Age"" AS Age,
    e.""Mask"" AS Mask,
    e.""CreatedAt"" AS CreatedAt,
    true AS HasFeature,
    (1 - (i.feature_vector <=> src.feature_vector)) AS TraceSimilarity
FROM {quotedIndexTable} src
JOIN {quotedIndexTable} i ON i.event_id <> src.event_id
JOIN {EventsTable} e ON e.""Id"" = i.event_id
LEFT JOIN {CamerasTable} c ON c.camera_id = e.""CameraId""
{whereClause}
ORDER BY i.feature_vector <=> src.feature_vector
LIMIT @Limit;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        ApplyVectorSearchSettings(connection);

        var items = (await connection.QueryAsync<FaceEventSearchRow>(sql, parameters)).ToList();
        return new FaceEventSearchResult(items, items.Count);
    }

    private static (int Limit, int Offset) ResolvePaging(FaceEventSearchFilter filter)
    {
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var page = Math.Max(1, filter.Page);
        var offset = (page - 1) * pageSize;
        return (pageSize, offset);
    }

    private static string BuildWhereClause(
        FaceEventSearchFilter filter,
        DynamicParameters parameters,
        string? hasFeatureExpression,
        bool includeWatchlistIds)
    {
        var clauses = new List<string>();

        if (filter.FromUtc.HasValue)
        {
            clauses.Add("e.\"EventTimeUtc\" >= @FromUtc");
            parameters.Add("FromUtc", filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            clauses.Add("e.\"EventTimeUtc\" <= @ToUtc");
            parameters.Add("ToUtc", filter.ToUtc.Value);
        }

        if (filter.CameraIds is { Count: > 0 })
        {
            var ids = filter.CameraIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            if (ids.Length > 0)
            {
                clauses.Add("e.\"CameraId\" = ANY(@CameraIds)");
                parameters.Add("CameraIds", ids);
            }
        }

        if (includeWatchlistIds && filter.WatchlistEntryIds is { Count: > 0 })
        {
            var ids = filter.WatchlistEntryIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            if (ids.Length > 0)
            {
                clauses.Add("e.\"WatchlistEntryId\" = ANY(@WatchlistEntryIds)");
                parameters.Add("WatchlistEntryIds", ids);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.ListType))
        {
            var listType = filter.ListType.Trim();
            if (string.Equals(listType, "Undefined", StringComparison.OrdinalIgnoreCase))
            {
                clauses.Add("e.\"IsKnown\" = false");
            }
            else if (string.Equals(listType, "Protect", StringComparison.OrdinalIgnoreCase))
            {
                clauses.Add("e.\"IsKnown\" = true");
                clauses.Add("(e.\"PersonJson\"->>'listType' IS NULL OR e.\"PersonJson\"->>'listType' = '')");
            }
            else
            {
                clauses.Add("e.\"IsKnown\" = true");
                clauses.Add("lower(e.\"PersonJson\"->>'listType') = @ListType");
                parameters.Add("ListType", listType.ToLowerInvariant());
            }
        }
        else if (filter.IsKnown.HasValue)
        {
            clauses.Add("e.\"IsKnown\" = @IsKnown");
            parameters.Add("IsKnown", filter.IsKnown.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Gender))
        {
            clauses.Add("lower(e.\"Gender\") = @Gender");
            parameters.Add("Gender", filter.Gender.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(filter.Mask))
        {
            clauses.Add("lower(e.\"Mask\") = @Mask");
            parameters.Add("Mask", filter.Mask.Trim().ToLowerInvariant());
        }

        if (filter.AgeMin.HasValue)
        {
            clauses.Add("e.\"Age\" >= @AgeMin");
            parameters.Add("AgeMin", filter.AgeMin.Value);
        }

        if (filter.AgeMax.HasValue)
        {
            clauses.Add("e.\"Age\" <= @AgeMax");
            parameters.Add("AgeMax", filter.AgeMax.Value);
        }

        if (filter.ScoreMin.HasValue)
        {
            clauses.Add("e.\"Score\" >= @ScoreMin");
            parameters.Add("ScoreMin", filter.ScoreMin.Value);
        }

        if (filter.SimilarityMin.HasValue)
        {
            clauses.Add("e.\"Similarity\" >= @SimilarityMin");
            parameters.Add("SimilarityMin", filter.SimilarityMin.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.PersonQuery))
        {
            var term = "%" + filter.PersonQuery.Trim() + "%";
            clauses.Add(@"(
    COALESCE(e.""PersonJson""->>'code', '') ILIKE @PersonQuery
    OR COALESCE(e.""PersonJson""->>'firstName', '') ILIKE @PersonQuery
    OR COALESCE(e.""PersonJson""->>'lastName', '') ILIKE @PersonQuery
)");
            parameters.Add("PersonQuery", term);
        }

        if (filter.PersonIds is { Count: > 0 })
        {
            var ids = filter.PersonIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            if (ids.Length > 0)
            {
                clauses.Add($@"(
    e.""PersonId"" = ANY(@PersonIds)
    OR EXISTS (
        SELECT 1
        FROM {TemplatesTable} ft
        WHERE ft.""Id""::text = e.""WatchlistEntryId""
          AND ft.""PersonId""::text = ANY(@PersonIds)
    )
)");
                parameters.Add("PersonIds", ids);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            clauses.Add("COALESCE(e.\"PersonJson\"->>'category', '') ILIKE @Category");
            parameters.Add("Category", "%" + filter.Category.Trim() + "%");
        }

        if (filter.HasFeature.HasValue && !string.IsNullOrWhiteSpace(hasFeatureExpression))
        {
            var expr = $"({hasFeatureExpression})";
            clauses.Add(filter.HasFeature.Value ? expr : $"NOT {expr}");
        }

        return clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
    }

    private static string BuildVectorLiteral(float[] vector)
    {
        if (vector.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var value = vector[i];
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return string.Empty;
            }

            builder.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }
        builder.Append(']');
        return builder.ToString();
    }

    private void ApplyVectorSearchSettings(NpgsqlConnection connection)
    {
        if (_vectorOptions.HnswEfSearch <= 0)
        {
            return;
        }

        var efSearch = _vectorOptions.HnswEfSearch.ToString(CultureInfo.InvariantCulture);
        connection.Execute($"SET hnsw.ef_search = {efSearch};");
    }

    private static string BuildHasFeatureExpression(
        IReadOnlyList<FaceEventIndexTable> tables,
        string eventIdExpression)
    {
        if (tables.Count == 0)
        {
            return "false";
        }

        var clauses = new List<string>();
        foreach (var table in tables)
        {
            clauses.Add(
                $"EXISTS (SELECT 1 FROM {QuoteIdentifier(table.Name)} i WHERE i.event_id = {eventIdExpression})");
        }

        return "(" + string.Join(" OR ", clauses) + ")";
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    public PersonProfile? DeserializePerson(string? json)
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
            _logger.LogWarning(ex, "Failed to deserialize person payload for face event.");
            return null;
        }
    }
}

public sealed class FaceEventSearchFilter
{
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public IReadOnlyCollection<string>? CameraIds { get; init; }
    public bool? IsKnown { get; init; }
    public string? ListType { get; init; }
    public string? Gender { get; init; }
    public int? AgeMin { get; init; }
    public int? AgeMax { get; init; }
    public string? Mask { get; init; }
    public float? ScoreMin { get; init; }
    public float? SimilarityMin { get; init; }
    public string? PersonQuery { get; init; }
    public IReadOnlyCollection<string>? PersonIds { get; init; }
    public string? Category { get; init; }
    public bool? HasFeature { get; init; }
    public IReadOnlyCollection<string>? WatchlistEntryIds { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed record FaceEventSearchResult(IReadOnlyList<FaceEventSearchRow> Items, int Total);

public sealed class FaceEventSearchRow
{
    public Guid Id { get; init; }
    public DateTime EventTimeUtc { get; init; }
    public string CameraId { get; init; } = string.Empty;
    public string? CameraIp { get; init; }
    public string? CameraZone { get; init; }
    public bool IsKnown { get; init; }
    public string? WatchlistEntryId { get; init; }
    public string? PersonId { get; init; }
    public string? PersonJson { get; init; }
    public float? WatchlistSimilarity { get; init; }
    public float? Score { get; init; }
    public string? BestshotPath { get; init; }
    public string? Gender { get; init; }
    public int? Age { get; init; }
    public string? Mask { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool HasFeature { get; init; }
    public float? TraceSimilarity { get; init; }
}
