namespace LightJSC.Core.Models;

public sealed class MapCameraPosition
{
    public Guid MapId { get; set; }
    public string CameraId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public float? X { get; set; }
    public float? Y { get; set; }
    public float? AngleDegrees { get; set; }
    public float? FovDegrees { get; set; }
    public float? Range { get; set; }
    public float? IconScale { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime UpdatedAt { get; set; }
}
