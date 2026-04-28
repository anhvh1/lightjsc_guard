using System.Collections.Concurrent;
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

namespace LightJSC.Infrastructure.Vector;

public sealed class PostgresVectorIndex : IVectorIndex
{
    private const string BaseTableName = "watchlist_index";
    private const string TablePrefix = "watchlist_index%";
    private readonly string _connectionString;
    private readonly VectorIndexOptions _options;
    private readonly ILogger<PostgresVectorIndex> _logger;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, string> _tablesByModel = new();

    public PostgresVectorIndex(
        IConfiguration configuration,
        IOptions<VectorIndexOptions> options,
        ILogger<PostgresVectorIndex> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? configuration["Postgres:ConnectionString"]
            ?? string.Empty;
        _options = options.Value;
        _logger = logger;
    }

    public int Count => GetCount();

    public void AddOrUpdate(WatchlistEntry entry)
    {
        if (entry.FeatureVector.Length == 0)
        {
            Remove(entry.EntryId);
            return;
        }

        var tableName = EnsureTable(entry.FeatureVersion, entry.FeatureVector.Length);
        if (string.IsNullOrWhiteSpace(tableName))
        {
            _logger.LogWarning(
                "Vector dimension mismatch. Configured={Configured} Actual={Actual} Entry={EntryId}",
                _options.Dimension,
                entry.FeatureVector.Length,
                entry.EntryId);
            return;
        }

        var vectorText = BuildVectorLiteral(entry.FeatureVector);
        if (string.IsNullOrWhiteSpace(vectorText))
        {
            return;
        }

        var personJson = entry.Person is null
            ? null
            : JsonSerializer.Serialize(entry.Person, _jsonOptions);

        var sql = $@"
INSERT INTO {QuoteIdentifier(tableName)}
    (entry_id, person_id, person_json, feature_bytes, feature_vector, similarity_threshold, updated_at, is_active)
VALUES
    (@EntryId, @PersonId, @PersonJson::jsonb, @FeatureBytes, @FeatureVector::vector, @SimilarityThreshold, @UpdatedAt, @IsActive)
ON CONFLICT (entry_id) DO UPDATE SET
    person_id = EXCLUDED.person_id,
    person_json = EXCLUDED.person_json,
    feature_bytes = EXCLUDED.feature_bytes,
    feature_vector = EXCLUDED.feature_vector,
    similarity_threshold = EXCLUDED.similarity_threshold,
    updated_at = EXCLUDED.updated_at,
    is_active = EXCLUDED.is_active;";

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var commandTimeout = ResolveCommandTimeout();
        connection.Execute(sql, new
        {
            EntryId = entry.EntryId,
            PersonId = entry.PersonId,
            PersonJson = personJson,
            FeatureBytes = entry.FeatureBytes,
            FeatureVector = vectorText,
            SimilarityThreshold = entry.SimilarityThreshold,
            UpdatedAt = entry.UpdatedAt,
            IsActive = entry.IsActive
        }, commandTimeout: commandTimeout);
    }

    public bool Remove(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        var tables = GetIndexTables();
        if (tables.Count == 0)
        {
            return false;
        }

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var commandTimeout = ResolveCommandTimeout();

        var removed = 0;
        foreach (var table in tables)
        {
            removed += connection.Execute(
                $"DELETE FROM {QuoteIdentifier(table.Name)} WHERE entry_id = @EntryId",
                new { EntryId = entryId },
                commandTimeout: commandTimeout);
        }

        return removed > 0;
    }

    public IReadOnlyList<VectorMatch> SearchTopK(float[] vector, string? featureVersion, int topK)
    {
        if (vector.Length == 0 || topK <= 0)
        {
            return Array.Empty<VectorMatch>();
        }

        var tableName = EnsureTable(featureVersion, vector.Length);
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return Array.Empty<VectorMatch>();
        }

        var vectorText = BuildVectorLiteral(vector);
        if (string.IsNullOrWhiteSpace(vectorText))
        {
            return Array.Empty<VectorMatch>();
        }

        var sql = $@"
SELECT
    entry_id AS EntryId,
    person_id AS PersonId,
    person_json AS PersonJson,
    similarity_threshold AS SimilarityThreshold,
    (1 - (feature_vector <=> @FeatureVector::vector)) AS similarity
FROM {QuoteIdentifier(tableName)}
WHERE is_active = true
ORDER BY feature_vector <=> @FeatureVector::vector
LIMIT @TopK;";

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        if (_options.HnswEfSearch > 0)
        {
            var efSearch = _options.HnswEfSearch.ToString(CultureInfo.InvariantCulture);
            connection.Execute($"SET hnsw.ef_search = {efSearch};", commandTimeout: ResolveCommandTimeout());
        }

        var rows = connection.Query<VectorMatchRow>(sql, new
        {
            FeatureVector = vectorText,
            TopK = topK
        }, commandTimeout: ResolveCommandTimeout());

        var results = new List<VectorMatch>();
        foreach (var row in rows)
        {
            var person = DeserializePerson(row.PersonJson);
            var entry = new WatchlistEntry
            {
                EntryId = row.EntryId,
                PersonId = row.PersonId,
                Person = person,
                FeatureVersion = featureVersion ?? string.Empty,
                SimilarityThreshold = row.SimilarityThreshold
            };
            results.Add(new VectorMatch(row.EntryId, row.Similarity, row.SimilarityThreshold, entry));
        }

        return results;
    }

    private int GetCount()
    {
        var tables = GetIndexTables();
        if (tables.Count == 0)
        {
            return 0;
        }

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var commandTimeout = ResolveCommandTimeout();

        var total = 0;
        foreach (var table in tables)
        {
            total += connection.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {QuoteIdentifier(table.Name)} WHERE is_active = true",
                commandTimeout: commandTimeout);
        }

        return total;
    }

    private string? EnsureTable(string? featureVersion, int vectorLength)
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

        _schemaLock.Wait();
        try
        {
            if (_tablesByModel.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            connection.Execute("CREATE EXTENSION IF NOT EXISTS vector;", commandTimeout: ResolveCommandTimeout());

            var tables = LoadIndexTables(connection);
            UpdateCache(tables);

            if (_tablesByModel.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            var desiredDimension = ResolveDimension(vectorLength);
            if (desiredDimension <= 0)
            {
                _logger.LogWarning("Vector dimension is not configured; skip vector index initialization.");
                return null;
            }

            var tableName = ResolveNewTableName(tables, modelKey, desiredDimension);
            CreateTable(connection, tableName, desiredDimension);
            _tablesByModel[cacheKey] = tableName;
            return tableName;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private IReadOnlyList<IndexTableInfo> GetIndexTables()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var tables = LoadIndexTables(connection);
        UpdateCache(tables);
        return tables;
    }

    private int ResolveDimension(int vectorLength)
    {
        if (vectorLength > 0)
        {
            if (_options.Dimension > 0 && _options.Dimension != vectorLength)
            {
                _logger.LogWarning(
                    "Watchlist vector dimension differs from VectorIndex:Dimension. Configured={Configured} VectorLength={VectorLength}. Using entry dimension.",
                    _options.Dimension,
                    vectorLength);
            }

            return vectorLength;
        }

        return _options.Dimension;
    }

    private static string ResolveNewTableName(IReadOnlyList<IndexTableInfo> tables, string modelKey, int dimension)
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

    private void CreateTable(NpgsqlConnection connection, string tableName, int dimension)
    {
        var quotedTable = QuoteIdentifier(tableName);
        var createTableSql = $@"
CREATE TABLE IF NOT EXISTS {quotedTable} (
    entry_id TEXT PRIMARY KEY,
    person_id TEXT NULL,
    person_json JSONB NULL,
    feature_bytes BYTEA NOT NULL,
    feature_vector vector({dimension}) NOT NULL,
    similarity_threshold REAL NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true
);";
        connection.Execute(createTableSql, commandTimeout: ResolveCommandTimeout());

        var updatedAtIndex = QuoteIdentifier($"ix_{tableName}_updated_at");
        var isActiveIndex = QuoteIdentifier($"ix_{tableName}_is_active");
        var vectorIndex = QuoteIdentifier($"ix_{tableName}_vector_hnsw");
        connection.Execute($@"
CREATE INDEX IF NOT EXISTS {updatedAtIndex}
    ON {quotedTable} (updated_at);
CREATE INDEX IF NOT EXISTS {isActiveIndex}
    ON {quotedTable} (is_active);
CREATE INDEX IF NOT EXISTS {vectorIndex}
    ON {quotedTable} USING hnsw (feature_vector vector_cosine_ops)
    WITH (m = {_options.HnswM}, ef_construction = {_options.HnswEfConstruction})
    WHERE is_active = true;", commandTimeout: ResolveCommandTimeout());
    }

    private IReadOnlyList<IndexTableInfo> LoadIndexTables(NpgsqlConnection connection)
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

        var rows = connection.Query<IndexTableRow>(
            sql,
            new { Prefix = TablePrefix },
            commandTimeout: ResolveCommandTimeout());
        var tables = new List<IndexTableInfo>();
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

            tables.Add(new IndexTableInfo(row.Name, row.Dimension, modelKey));
        }

        return tables;
    }

    private void UpdateCache(IReadOnlyList<IndexTableInfo> tables)
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

    private int? ResolveCommandTimeout()
    {
        if (_options.QueryTimeoutSeconds <= 0)
        {
            return null;
        }

        return _options.QueryTimeoutSeconds;
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
            _logger.LogWarning(ex, "Failed to deserialize watchlist person payload.");
            return null;
        }
    }

    private sealed record IndexTableInfo(string Name, int Dimension, string ModelKey);

    private sealed class IndexTableRow
    {
        public string Name { get; init; } = string.Empty;
        public int Dimension { get; init; }
    }

    private sealed class VectorMatchRow
    {
        public string EntryId { get; init; } = string.Empty;
        public string? PersonId { get; init; }
        public string? PersonJson { get; init; }
        public float SimilarityThreshold { get; init; }
        public float Similarity { get; init; }
    }
}
