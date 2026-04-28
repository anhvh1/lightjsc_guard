namespace LightJSC.Core.Options;

public sealed class MapOptions
{
    public string RootPath { get; set; } = "maps";
    public int MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public string? GeoStyleUrl { get; set; }
    public string? RoutingBaseUrl { get; set; }
    public bool ServeLocalAssets { get; set; } = true;
}
