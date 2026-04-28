namespace LightJSC.Core.Options;

public sealed class WatchlistOptions
{
    public string Source { get; set; } = "Local";
    public int SyncIntervalSeconds { get; set; } = 20;
    public int FullRefreshMinutes { get; set; } = 10;
    public bool UsePerItemThreshold { get; set; } = true;
    public string FeatureVersion { get; set; } = "legacy";
}

