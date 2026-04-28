namespace LightJSC.Api.Contracts;

public sealed class MapLayoutRequest
{
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Image";
}

public sealed class MapLayoutResponse
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    public double? GeoCenterLatitude { get; set; }
    public double? GeoCenterLongitude { get; set; }
    public double? GeoZoom { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class MapCameraPositionRequest
{
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
}

public sealed class MapCameraPositionResponse
{
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

public sealed class MapDetailResponse
{
    public MapLayoutResponse Map { get; set; } = new();
    public List<MapCameraPositionResponse> Cameras { get; set; } = new();
}

public sealed class MapViewRequest
{
    public double GeoCenterLatitude { get; set; }
    public double GeoCenterLongitude { get; set; }
    public double GeoZoom { get; set; }
}

public sealed class MapRouteRequest
{
    public List<LightJSC.Core.Models.GeoPoint> Points { get; set; } = new();
    public string? Mode { get; set; }
}

public sealed class MapRouteResponse
{
    public List<LightJSC.Core.Models.GeoPoint> Points { get; set; } = new();
    public bool IsFallback { get; set; }
}

public sealed class MapOptionsResponse
{
    public string? GeoStyleUrl { get; set; }
    public bool RoutingEnabled { get; set; }
}

// GeoPoint moved to LightJSC.Core.Models.GeoPoint
