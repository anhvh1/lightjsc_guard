using System.Globalization;
using System.Net.Mime;
using System.Text.Json;
using LightJSC.Api.Contracts;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/maps")]
public sealed class MapsController : ControllerBase
{
    private readonly IMapRepository _mapRepository;
    private readonly ICameraRepository _cameraRepository;
    private readonly MapOptions _options;
    private readonly RoutingOptions _routingOptions;
    private readonly IRoutingService _routingService;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MapsController> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public MapsController(
        IMapRepository mapRepository,
        ICameraRepository cameraRepository,
        IOptions<MapOptions> options,
        IOptions<RoutingOptions> routingOptions,
        IRoutingService routingService,
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        ILogger<MapsController> logger)
    {
        _mapRepository = mapRepository;
        _cameraRepository = cameraRepository;
        _options = options.Value;
        _routingOptions = routingOptions.Value;
        _routingService = routingService;
        _environment = environment;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MapLayoutResponse>>> List(CancellationToken cancellationToken)
    {
        var maps = await _mapRepository.ListAsync(cancellationToken);
        return Ok(maps.Select(ToResponse).ToList());
    }

    [HttpGet("options")]
    public ActionResult<MapOptionsResponse> GetOptions()
    {
        return Ok(new MapOptionsResponse
        {
            GeoStyleUrl = string.IsNullOrWhiteSpace(_options.GeoStyleUrl) ? null : _options.GeoStyleUrl,
            RoutingEnabled = _routingOptions.Enabled || !string.IsNullOrWhiteSpace(_options.RoutingBaseUrl)
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MapDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetAsync(id, cancellationToken);
        if (map is null)
        {
            return NotFound();
        }

        var positions = await _mapRepository.ListPositionsAsync(id, cancellationToken);
        return Ok(new MapDetailResponse
        {
            Map = ToResponse(map),
            Cameras = positions.Select(ToResponse).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<MapLayoutResponse>> Create(
        [FromBody] MapLayoutRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var mapType = NormalizeType(request.Type);
        if (mapType is null)
        {
            return BadRequest("Type must be Image or Geo.");
        }

        if (request.ParentId.HasValue)
        {
            var parent = await _mapRepository.GetAsync(request.ParentId.Value, cancellationToken);
            if (parent is null)
            {
                return BadRequest("Parent map was not found.");
            }
        }

        var now = DateTime.UtcNow;
        var map = new MapLayout
        {
            Id = Guid.NewGuid(),
            ParentId = request.ParentId,
            Name = request.Name.Trim(),
            Type = mapType,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _mapRepository.AddAsync(map, cancellationToken);
        return Ok(ToResponse(map));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MapLayoutResponse>> Update(
        Guid id,
        [FromBody] MapLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetAsync(id, cancellationToken);
        if (map is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        var mapType = NormalizeType(request.Type);
        if (mapType is null)
        {
            return BadRequest("Type must be Image or Geo.");
        }

        if (request.ParentId.HasValue)
        {
            var parent = await _mapRepository.GetAsync(request.ParentId.Value, cancellationToken);
            if (parent is null)
            {
                return BadRequest("Parent map was not found.");
            }
        }

        if (request.ParentId == map.Id)
        {
            return BadRequest("Parent map cannot be the same map.");
        }

        map.ParentId = request.ParentId;
        map.Name = request.Name.Trim();
        map.Type = mapType;
        map.UpdatedAt = DateTime.UtcNow;

        await _mapRepository.UpdateAsync(map, cancellationToken);
        return Ok(ToResponse(map));
    }

    [HttpPut("{id:guid}/view")]
    public async Task<ActionResult<MapLayoutResponse>> UpdateView(
        Guid id,
        [FromBody] MapViewRequest request,
        CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetAsync(id, cancellationToken);
        if (map is null)
        {
            return NotFound();
        }

        if (!string.Equals(map.Type, MapLayoutTypes.Geo, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Map view settings are only supported for Geo maps.");
        }

        if (!IsValidGeo(request.GeoCenterLatitude, request.GeoCenterLongitude))
        {
            return BadRequest("Latitude/Longitude are out of range.");
        }

        if (request.GeoZoom is < 0 or > 24)
        {
            return BadRequest("GeoZoom must be between 0 and 24.");
        }

        map.GeoCenterLatitude = request.GeoCenterLatitude;
        map.GeoCenterLongitude = request.GeoCenterLongitude;
        map.GeoZoom = request.GeoZoom;
        map.UpdatedAt = DateTime.UtcNow;

        await _mapRepository.UpdateAsync(map, cancellationToken);
        return Ok(ToResponse(map));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mapRepository.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetAsync(id, cancellationToken);
        if (map is null || string.IsNullOrWhiteSpace(map.ImagePath))
        {
            return NotFound();
        }

        var fullPath = ResolveImagePath(map.ImagePath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var contentType = ResolveContentType(Path.GetExtension(fullPath));
        return PhysicalFile(fullPath, contentType);
    }

    [HttpPost("{id:guid}/image")]
    public async Task<ActionResult<MapLayoutResponse>> UploadImage(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("File is required.");
        }

        if (file.Length > _options.MaxImageBytes)
        {
            return BadRequest($"File exceeds max size {_options.MaxImageBytes} bytes.");
        }

        var map = await _mapRepository.GetAsync(id, cancellationToken);
        if (map is null)
        {
            return NotFound();
        }

        var extension = Path.GetExtension(file.FileName);
        if (!IsAllowedImageExtension(extension))
        {
            return BadRequest("Only .png, .jpg, or .jpeg files are supported.");
        }

        var relativePath = BuildRelativeImagePath(id, extension);
        var fullPath = ResolveImagePath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ResolveRootPath());
        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        map.ImagePath = relativePath;
        map.UpdatedAt = DateTime.UtcNow;

        await _mapRepository.UpdateAsync(map, cancellationToken);
        return Ok(ToResponse(map));
    }

    [HttpPut("{id:guid}/cameras")]
    public async Task<ActionResult<MapDetailResponse>> ReplaceCameras(
        Guid id,
        [FromBody] List<MapCameraPositionRequest> positions,
        CancellationToken cancellationToken)
    {
        var map = await _mapRepository.GetAsync(id, cancellationToken);
        if (map is null)
        {
            return NotFound();
        }

        var availableCameras = await _cameraRepository.ListAsync(cancellationToken);
        var cameraIds = new HashSet<string>(availableCameras.Select(x => x.CameraId), StringComparer.OrdinalIgnoreCase);

        var invalid = positions.Where(x => !cameraIds.Contains(x.CameraId)).Select(x => x.CameraId).ToList();
        if (invalid.Count > 0)
        {
            return BadRequest($"Unknown camera(s): {string.Join(", ", invalid)}");
        }

        var normalized = new List<MapCameraPosition>();
        foreach (var position in positions)
        {
            if (!IsValidPosition(map.Type, position, out var error))
            {
                return BadRequest(error);
            }

            normalized.Add(new MapCameraPosition
            {
                MapId = map.Id,
                CameraId = position.CameraId.Trim(),
                Label = string.IsNullOrWhiteSpace(position.Label) ? null : position.Label.Trim(),
                X = position.X,
                Y = position.Y,
                AngleDegrees = position.AngleDegrees,
                FovDegrees = position.FovDegrees,
                Range = position.Range,
                IconScale = position.IconScale,
                Latitude = position.Latitude,
                Longitude = position.Longitude,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _mapRepository.ReplacePositionsAsync(map.Id, normalized, cancellationToken);
        var stored = await _mapRepository.ListPositionsAsync(map.Id, cancellationToken);
        return Ok(new MapDetailResponse
        {
            Map = ToResponse(map),
            Cameras = stored.Select(ToResponse).ToList()
        });
    }

    [HttpPost("route")]
    public async Task<ActionResult<MapRouteResponse>> BuildRoute(
        [FromBody] MapRouteRequest request,
        CancellationToken cancellationToken)
    {
        var points = request.Points
            .Where(point => IsValidGeo(point.Latitude, point.Longitude))
            .ToList();

        if (points.Count < 2)
        {
            return Ok(new MapRouteResponse { Points = points, IsFallback = true });
        }

        // Prefer local routing service when enabled
        if (_routingOptions.Enabled)
        {
            var result = await _routingService.BuildRouteAsync(points, request.Mode, cancellationToken);
            return Ok(new MapRouteResponse
            {
                Points = result.Points,
                IsFallback = result.IsFallback
            });
        }

        var baseUrl = _options.RoutingBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return Ok(new MapRouteResponse { Points = points, IsFallback = true });
        }

        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "driving" : request.Mode.Trim();
        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = BuildRouteUrl(baseUrl, mode, points);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Routing service returned {StatusCode}.", response.StatusCode);
                return Ok(new MapRouteResponse { Points = points, IsFallback = true });
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var route = await JsonSerializer.DeserializeAsync<RouteResponse>(stream, _jsonOptions, cancellationToken);
            var geometry = route?.Routes?.FirstOrDefault()?.Geometry?.Coordinates;
            if (geometry is null || geometry.Count == 0)
            {
                return Ok(new MapRouteResponse { Points = points, IsFallback = true });
            }

            var decoded = geometry
                .Select(coord => new GeoPoint
                {
                    Latitude = coord[1],
                    Longitude = coord[0]
                })
                .ToList();

            return Ok(new MapRouteResponse
            {
                Points = decoded,
                IsFallback = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Routing request failed.");
            return Ok(new MapRouteResponse { Points = points, IsFallback = true });
        }
    }

    private MapLayoutResponse ToResponse(MapLayout map)
    {
        return new MapLayoutResponse
        {
            Id = map.Id,
            ParentId = map.ParentId,
            Name = map.Name,
            Type = map.Type,
            ImageUrl = string.IsNullOrWhiteSpace(map.ImagePath) ? null : $"/api/v1/maps/{map.Id}/image",
            ImageWidth = map.ImageWidth,
            ImageHeight = map.ImageHeight,
            GeoCenterLatitude = map.GeoCenterLatitude,
            GeoCenterLongitude = map.GeoCenterLongitude,
            GeoZoom = map.GeoZoom,
            CreatedAt = map.CreatedAt,
            UpdatedAt = map.UpdatedAt
        };
    }

    private static MapCameraPositionResponse ToResponse(MapCameraPosition position)
    {
        return new MapCameraPositionResponse
        {
            CameraId = position.CameraId,
            Label = position.Label,
            X = position.X,
            Y = position.Y,
            AngleDegrees = position.AngleDegrees,
            FovDegrees = position.FovDegrees,
            Range = position.Range,
            IconScale = position.IconScale,
            Latitude = position.Latitude,
            Longitude = position.Longitude,
            UpdatedAt = position.UpdatedAt
        };
    }

    private static string? NormalizeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value, MapLayoutTypes.Image, StringComparison.OrdinalIgnoreCase))
        {
            return MapLayoutTypes.Image;
        }

        if (string.Equals(value, MapLayoutTypes.Geo, StringComparison.OrdinalIgnoreCase))
        {
            return MapLayoutTypes.Geo;
        }

        return null;
    }

    private string ResolveRootPath()
    {
        var root = string.IsNullOrWhiteSpace(_options.RootPath)
            ? "maps"
            : _options.RootPath.Trim();

        return Path.IsPathRooted(root)
            ? root
            : Path.Combine(_environment.ContentRootPath, root);
    }

    private string ResolveImagePath(string relativePath)
    {
        return Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(ResolveRootPath(), relativePath);
    }

    private static string BuildRelativeImagePath(Guid mapId, string extension)
    {
        var safeExt = extension.ToLowerInvariant();
        return Path.Combine(mapId.ToString("N"), $"map{safeExt}");
    }

    private static bool IsAllowedImageExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => MediaTypeNames.Image.Png,
            ".jpg" => MediaTypeNames.Image.Jpeg,
            ".jpeg" => MediaTypeNames.Image.Jpeg,
            _ => MediaTypeNames.Application.Octet
        };
    }

    private static bool IsValidPosition(string mapType, MapCameraPositionRequest position, out string? error)
    {
        error = null;

        if (position.IconScale.HasValue &&
            (position.IconScale.Value < 0.2f || position.IconScale.Value > 5f))
        {
            error = "IconScale must be between 0.2 and 5.";
            return false;
        }

        if (position.AngleDegrees.HasValue &&
            (position.AngleDegrees.Value < 0f || position.AngleDegrees.Value >= 360f))
        {
            error = "AngleDegrees must be between 0 and 360.";
            return false;
        }

        if (position.FovDegrees.HasValue &&
            (position.FovDegrees.Value < 5f || position.FovDegrees.Value > 180f))
        {
            error = "FovDegrees must be between 5 and 180.";
            return false;
        }

        if (position.Range.HasValue && position.Range.Value <= 0f)
        {
            error = "Range must be greater than 0.";
            return false;
        }

        if (string.Equals(mapType, MapLayoutTypes.Geo, StringComparison.OrdinalIgnoreCase))
        {
            if (!position.Latitude.HasValue || !position.Longitude.HasValue)
            {
                error = "Latitude and Longitude are required for Geo maps.";
                return false;
            }

            if (!IsValidGeo(position.Latitude.Value, position.Longitude.Value))
            {
                error = "Latitude/Longitude are out of range.";
                return false;
            }

            return true;
        }

        if (position.Range.HasValue && position.Range.Value > 1f)
        {
            error = "Range must be between 0 and 1 for Image maps.";
            return false;
        }

        if (!position.X.HasValue || !position.Y.HasValue)
        {
            error = "X and Y are required for Image maps.";
            return false;
        }

        if (position.X < 0f || position.X > 1f || position.Y < 0f || position.Y > 1f)
        {
            error = "X and Y must be between 0 and 1.";
            return false;
        }

        return true;
    }

    private static bool IsValidGeo(double latitude, double longitude)
    {
        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static string BuildRouteUrl(string baseUrl, string mode, IReadOnlyList<GeoPoint> points)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var coords = string.Join(
            ";",
            points.Select(p =>
                $"{p.Longitude.ToString("F6", CultureInfo.InvariantCulture)},{p.Latitude.ToString("F6", CultureInfo.InvariantCulture)}"));
        return $"{trimmed}/route/v1/{mode}/{coords}?overview=full&geometries=geojson";
    }

    private sealed class RouteResponse
    {
        public List<RouteFeature>? Routes { get; set; }
    }

    private sealed class RouteFeature
    {
        public RouteGeometry? Geometry { get; set; }
    }

    private sealed class RouteGeometry
    {
        public List<double[]> Coordinates { get; set; } = new();
    }
}
