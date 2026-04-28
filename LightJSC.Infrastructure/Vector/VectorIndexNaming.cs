using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LightJSC.Infrastructure.Vector;

internal static class VectorIndexNaming
{
    public const string LegacyModelKey = "legacy";
    private const int MaxModelKeyLength = 24;
    private const string Separator = "__";

    public static string NormalizeFeatureVersion(string? featureVersion)
    {
        if (string.IsNullOrWhiteSpace(featureVersion))
        {
            return LegacyModelKey;
        }

        var value = featureVersion.Trim();
        var builder = new StringBuilder(value.Length);
        var lastWasUnderscore = false;

        foreach (var ch in value)
        {
            if (ch <= 127 && char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasUnderscore = false;
                continue;
            }

            if (!lastWasUnderscore)
            {
                builder.Append('_');
                lastWasUnderscore = true;
            }
        }

        var cleaned = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return LegacyModelKey;
        }

        if (cleaned.Length > MaxModelKeyLength)
        {
            var hash = ShortHash(value);
            var maxPrefix = Math.Max(1, MaxModelKeyLength - hash.Length - 1);
            cleaned = cleaned[..maxPrefix] + "_" + hash;
        }

        return cleaned;
    }

    public static string BuildTableName(string baseName, string modelKey, int dimension)
    {
        var normalized = NormalizeFeatureVersion(modelKey);
        return $"{baseName}{Separator}{normalized}{Separator}{dimension}";
    }

    public static bool TryParseModelKey(string tableName, string baseName, out string modelKey)
    {
        modelKey = LegacyModelKey;
        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(baseName))
        {
            return false;
        }

        if (string.Equals(tableName, baseName, StringComparison.OrdinalIgnoreCase))
        {
            modelKey = LegacyModelKey;
            return true;
        }

        if (tableName.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = tableName[(baseName.Length + 1)..];
            if (suffix.Length > 0 && suffix.All(char.IsDigit))
            {
                modelKey = LegacyModelKey;
                return true;
            }
        }

        var prefix = baseName + Separator;
        if (!tableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = tableName[prefix.Length..];
        var splitIndex = remainder.IndexOf(Separator, StringComparison.Ordinal);
        if (splitIndex <= 0)
        {
            return false;
        }

        var candidate = remainder[..splitIndex];
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        modelKey = candidate;
        return true;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
    }
}
