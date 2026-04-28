namespace LightJSC.Core.Models;

public sealed class MapLayout
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = MapLayoutTypes.Image;
    public string? ImagePath { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public double? GeoCenterLatitude { get; set; }
    public double? GeoCenterLongitude { get; set; }
    public double? GeoZoom { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
