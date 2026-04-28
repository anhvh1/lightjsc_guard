using System.Text;

namespace LightJSC.Core.Helpers;

public static class FeatureVersionHelper
{
    private const char SeriesSeparator = '@';

    public static string Combine(string? featureVersion, string? cameraSeries)
    {
        var baseVersion = string.IsNullOrWhiteSpace(featureVersion) ? string.Empty : featureVersion.Trim();
        if (baseVersion.Contains(SeriesSeparator))
        {
            return baseVersion;
        }

        var series = NormalizeSeries(cameraSeries);
        if (string.IsNullOrWhiteSpace(series))
        {
            return baseVersion;
        }

        return string.IsNullOrWhiteSpace(baseVersion)
            ? series
            : $"{baseVersion}{SeriesSeparator}{series}";
    }

    public static string NormalizeSeries(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}
