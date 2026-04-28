namespace LightJSC.Api.MapTiles;

public sealed class MapTileOptions
{
    public string MbtilesPath { get; set; } = "MapData/mbtiles/vietnam_basic.mbtiles";
    public int MinZoom { get; set; } = 5;
    public int MaxZoom { get; set; } = 14;
    public bool UseTmsCoordinates { get; set; } = true;
}
