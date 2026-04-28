using System.Text.RegularExpressions;

namespace LightJSC.Infrastructure.Data;

public static class SqlNameHelper
{
    private static readonly Regex NamePattern = new(@"^[A-Za-z0-9_\.]+$", RegexOptions.Compiled);

    public static string Table(string name)
    {
        if (!NamePattern.IsMatch(name))
        {
            throw new InvalidOperationException($"Invalid table name '{name}'.");
        }

        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(".", parts.Select(p => $"[{p}]")).Trim();
    }

    public static string Col(string name)
    {
        if (!NamePattern.IsMatch(name))
        {
            throw new InvalidOperationException($"Invalid column name '{name}'.");
        }

        return $"[{name}]";
    }
}

