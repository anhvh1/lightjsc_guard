using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Dapper;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LightJSC.Infrastructure.Vector;

public sealed class PostgresFaceEventIndex : IFaceEventIndex
{
    private const string BaseTableName = "face_event_index";
    private const string TablePrefix = "face_event_index%";
    private readonly string _connectionString;
    private readonly VectorIndexOptions _options;
    private readonly ILogger<PostgresFaceEventIndex> _logger;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _tablesByModel = new();

    public PostgresFaceEventIndex(
        IConfiguration configuration,
        IOptions<VectorIndexOptions> options,
        ILogger<PostgresFaceEventIndex> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["Postgres:ConnectionString"]
            ?? string.Empty;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> EnsureSchemaAsync(string? featureVersion, int vectorLength, CancellationToken cancellationToken)
    {
        if (vectorLength > 0)
        {
            var table = await ResolveTableNameAsync(featureVersion, vectorLength, cancellationToken);
            return !string.IsNullOrWhiteSpace(table);
        }

        var tables = await ListTablesAsync(cancellationToken);
        return tables.Count > 0;
    }

    public async Task<IReadOnlyList<FaceEventIndexTable>> ListTablesAsync(CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var tables = await LoadIndexTablesAsync(connection, cancellationToken);
        UpdateCache(tables);
        return tables;
    }

    public async Task<string?> ResolveTableNameAsync(string? featureVersion, int vectorLength, CancellationToken cancellationToken)
    {
        if (vectorLength <= 0)
        {
            return null;
        }

        var modelKey = VectorIndexNaming.NormalizeFeatureVersion(featureVersion);
        var cacheKey = BuildCacheKey(modelKey, vectorLength);
        if (_tablesByModel.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_tablesByModel.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS vector;");

            var tables = await LoadIndexTablesAsync(connection, cancellationToken);
            UpdateCache(tables);

            if (_tablesByModel.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            var desiredDimension = ResolveDimension(vectorLength);
            if (desiredDimension <= 0)
            {
                _logger.LogWarning("Vector dimension is not configured; skip face event index initialization.");
                return null;
            }

            var tableName = ResolveNewTableName(tables, modelKey, desiredDimension);
            await CreateTableAsync(connection, tableName, desiredDimension, cancellationToken);

            _tablesByModel[cacheKey] = tableName;
            return tableName;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<string?> ResolveTableForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = await LoadIndexTablesAsync(connection, cancellationToken);
        UpdateCache(tables);
        foreach (var table in tables)
        {
            var sql = $"SELECT 1 FROM {QuoteIdentifier(table.Name)} WHERE event_id = @EventId LIMIT 1;";
            var exists = await connection.ExecuteScalarAsync<int?>(sql, new { EventId = eventId });
            if (exists.HasValue)
            {
                return table.Name;
            }
        }

        return null;
    }

    public async Task UpsertAsync(FaceEventIndexEntry entry, CancellationToken cancellationToken)
    {
        if (entry.FeatureVector.Length == 0)
        {
            return;
        }

        var tableName = await ResolveTableNameAsync(entry.FeatureVersion, entry.FeatureVector.Length, cancellationToken);
        if (string.IsNullOrWhiteSpace(tableName))
        {
            _logger.LogWarning(
                "Face event vector dimension mismatch. Configured={Configured} Actual={Actual} EventId={EventId}",
                _options.Dimension,
                entry.FeatureVector.Length,
                entry.EventId);
            return;
        }

        var vectorText = BuildVectorLiteral(entry.FeatureVector);
        if (string.IsNullOrWhiteSpace(vectorText))
        {
            return;
        }

        var sql = $@"
INSERT INTO {QuoteIdentifier(tableName)}
    (event_id, event_time_utc, camera_id, feature_vector)
VALUES
    (@EventId, @EventTimeUtc, @CameraId, @FeatureVector::vector)
ON CONFLICT (event_id) DO UPDATE SET
    event_time_utc = EXCLUDED.event_time_utc,
    camera_id = EXCLUDED.camera_id,
    feature_vector = EXCLUDED.feature_vector;";

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new
        {
            EventId = entry.EventId,
            EventTimeUtc = entry.EventTimeUtc,
            CameraId = entry.CameraId,
            FeatureVector = vectorText
        });
    }

    public async Task DeleteByEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
        {
            return;
        }

        var tables = await ListTablesAsync(cancellationToken);
        if (tables.Count == 0)
        {
            return;
        }

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var table in tables)
        {
            await connection.ExecuteAsync(
                $"DELETE FROM {QuoteIdentifier(table.Name)} WHERE event_id = ANY(@EventIds);",
                new { EventIds = eventIds.ToArray() });
        }
    }

    private int ResolveDimension(int vectorLength)
    {
        if (vectorLength > 0)
        {
            if (_options.Dimension > 0 && _options.Dimension != vectorLength)
            {
                _logger.LogWarning(
                    "Face event vector dimension differs from VectorIndex:Dimension. Configured={Configured} Event={Event}. Using event dimension for face_event_index.",
                    _options.Dimension,
                    vectorLength);
            }

            return vectorLength;
        }

        return _options.Dimension;
    }

    private static string ResolveNewTableName(IReadOnlyList<FaceEventIndexTable> tables, string modelKey, int dimension)
    {
        if (string.Equals(modelKey, VectorIndexNaming.LegacyModelKey, StringComparison.OrdinalIgnoreCase))
        {
            var legacyTables = tables
                .Where(table => string.Equals(table.ModelKey, VectorIndexNaming.LegacyModelKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var matchingLegacy = legacyTables.FirstOrDefault(table => table.Dimension == dimension);
            if (matchingLegacy is not null)
            {
                return matchingLegacy.Name;
            }

            var hasBase = legacyTables.Any(table => string.Equals(table.Name, BaseTableName, StringComparison.OrdinalIgnoreCase));
            return hasBase ? $"{BaseTableName}_{dimension}" : BaseTableName;
        }

        return VectorIndexNaming.BuildTableName(BaseTableName, modelKey, dimension);
    }

    private async Task CreateTableAsync(
        NpgsqlConnection connection,
        string tableName,
        int dimension,
        CancellationToken cancellationToken)
    {
        var quotedTable = QuoteIdentifier(tableName);
        var createTableSql = $@"
CREATE TABLE IF NOT EXISTS {quotedTable} (
    event_id UUID PRIMARY KEY,
    event_time_utc TIMESTAMPTZ NOT NULL,
    camera_id TEXT NOT NULL,
    feature_vector vector({dimension}) NOT NULL
);";
        await connection.ExecuteAsync(createTableSql);

        var eventTimeIndex = QuoteIdentifier($"ix_{tableName}_event_time");
        var cameraIndex = QuoteIdentifier($"ix_{tableName}_camera_id");
        var vectorIndex = QuoteIdentifier($"ix_{tableName}_vector_hnsw");
        await connection.ExecuteAsync($@"
CREATE INDEX IF NOT EXISTS {eventTimeIndex}
    ON {quotedTable} (event_time_utc);
CREATE INDEX IF NOT EXISTS {cameraIndex}
    ON {quotedTable} (camera_id);
CREATE INDEX IF NOT EXISTS {vectorIndex}
    ON {quotedTable} USING hnsw (feature_vector vector_cosine_ops)
    WITH (m = {_options.HnswM}, ef_construction = {_options.HnswEfConstruction});");
    }

    private async Task<IReadOnlyList<FaceEventIndexTable>> LoadIndexTablesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT c.relname AS Name,
       a.atttypmod - 4 AS Dimension
FROM pg_class c
JOIN pg_attribute a ON a.attrelid = c.oid
WHERE c.relkind = 'r'
  AND c.relname LIKE @Prefix
  AND pg_table_is_visible(c.oid)
  AND a.attname = 'feature_vector'
  AND a.attnum > 0
  AND NOT a.attisdropped;";

        var rows = await connection.QueryAsync<IndexTableRow>(sql, new { Prefix = TablePrefix });
        var tables = new List<FaceEventIndexTable>();
        foreach (var row in rows)
        {
            if (row.Dimension <= 0 || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            if (!VectorIndexNaming.TryParseModelKey(row.Name, BaseTableName, out var modelKey))
            {
                modelKey = VectorIndexNaming.LegacyModelKey;
            }

            tables.Add(new FaceEventIndexTable(row.Name, row.Dimension, modelKey));
        }

        return tables;
    }

    private void UpdateCache(IReadOnlyList<FaceEventIndexTable> tables)
    {
        _tablesByModel.Clear();
        foreach (var table in tables)
        {
            if (table.Dimension <= 0 || string.IsNullOrWhiteSpace(table.Name))
            {
                continue;
            }

            var cacheKey = BuildCacheKey(table.ModelKey, table.Dimension);
            if (string.Equals(table.Name, BaseTableName, StringComparison.OrdinalIgnoreCase))
            {
                _tablesByModel[cacheKey] = table.Name;
                continue;
            }

            _tablesByModel.TryAdd(cacheKey, table.Name);
        }
    }

    private static string BuildCacheKey(string modelKey, int dimension)
    {
        return $"{modelKey}:{dimension}";
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

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    private sealed class IndexTableRow
    {
        public string Name { get; init; } = string.Empty;
        public int Dimension { get; init; }
    }
}
