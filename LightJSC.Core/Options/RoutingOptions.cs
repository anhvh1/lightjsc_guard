namespace LightJSC.Core.Options;

public sealed class RoutingOptions
{
    public bool Enabled { get; set; }
    public string PbfPath { get; set; } = string.Empty;
    public string CachePath { get; set; } = string.Empty;
    public string Profile { get; set; } = "car";
}
