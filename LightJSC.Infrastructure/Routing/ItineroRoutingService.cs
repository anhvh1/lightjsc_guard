using Itinero;
using Itinero.IO.Osm;
using Itinero.Profiles;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VehicleDefs = Itinero.Osm.Vehicles.Vehicle;

namespace LightJSC.Infrastructure.Routing;

public sealed class ItineroRoutingService : IRoutingService
{
    private readonly RoutingOptions _options;
    private readonly ILogger<ItineroRoutingService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Router? _router;

    public ItineroRoutingService(IOptions<RoutingOptions> options, ILogger<ItineroRoutingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RouteResult> BuildRouteAsync(IReadOnlyList<GeoPoint> points, string? mode, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || points.Count < 2)
        {
            return new RouteResult { Points = points.ToList(), IsFallback = true };
        }

        var router = await GetRouterAsync(cancellationToken);
        if (router is null)
        {
            return new RouteResult { Points = points.ToList(), IsFallback = true };
        }

        var profile = ResolveProfile(mode ?? _options.Profile);
        if (profile is null)
        {
            return new RouteResult { Points = points.ToList(), IsFallback = true };
        }

        try
        {
            var routePoints = new List<GeoPoint>();
            for (var i = 0; i < points.Count - 1; i++)
            {
                var from = points[i];
                var to = points[i + 1];
                var p1 = router.Resolve(profile, (float)from.Latitude, (float)from.Longitude);
                var p2 = router.Resolve(profile, (float)to.Latitude, (float)to.Longitude);
                var route = router.Calculate(profile, p1, p2);
                foreach (var shape in route.Shape)
                {
                    routePoints.Add(new GeoPoint
                    {
                        Latitude = shape.Latitude,
                        Longitude = shape.Longitude
                    });
                }
            }

            if (routePoints.Count == 0)
            {
                return new RouteResult { Points = points.ToList(), IsFallback = true };
            }

            return new RouteResult
            {
                Points = routePoints,
                IsFallback = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Routing failed.");
            return new RouteResult { Points = points.ToList(), IsFallback = true };
        }
    }

    private async Task<Router?> GetRouterAsync(CancellationToken cancellationToken)
    {
        if (_router is not null)
        {
            return _router;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_router is not null)
            {
                return _router;
            }

            if (!string.IsNullOrWhiteSpace(_options.CachePath) && File.Exists(_options.CachePath))
            {
                _router = new Router(RouterDb.Deserialize(File.OpenRead(_options.CachePath)));
                return _router;
            }

            if (string.IsNullOrWhiteSpace(_options.PbfPath) || !File.Exists(_options.PbfPath))
            {
                _logger.LogWarning("Routing PBF file not found at {PbfPath}", _options.PbfPath);
                return null;
            }

            var profile = ResolveProfile(_options.Profile);
            if (profile is null)
            {
                _logger.LogWarning("Routing profile {Profile} is not supported.", _options.Profile);
                return null;
            }

            var routerDb = new RouterDb();
            using (var stream = File.OpenRead(_options.PbfPath))
            {
                LoadOsm(routerDb, stream, profile);
            }

            if (!string.IsNullOrWhiteSpace(_options.CachePath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_options.CachePath) ?? string.Empty);
                    using var file = File.Open(_options.CachePath, FileMode.Create, FileAccess.Write);
                    routerDb.Serialize(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write routing cache to {CachePath}", _options.CachePath);
                }
            }

            _router = new Router(routerDb);
            return _router;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static Profile? ResolveProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return VehicleDefs.Car.Fastest();
        }

        var normalized = profile.Trim().ToLowerInvariant();
        return normalized switch
        {
            "car" or "drive" or "driving" => VehicleDefs.Car.Fastest(),
            "bike" or "bicycle" => VehicleDefs.Bicycle.Fastest(),
            "foot" or "walk" or "walking" => VehicleDefs.Pedestrian.Fastest(),
            _ => null
        };
    }

    private static void LoadOsm(RouterDb routerDb, Stream stream, Profile profile)
    {
        // Resolve the vehicle from the profile to load OSM data.
        if (profile == VehicleDefs.Car.Fastest())
        {
            routerDb.LoadOsmData(stream, VehicleDefs.Car);
            return;
        }

        if (profile == VehicleDefs.Bicycle.Fastest())
        {
            routerDb.LoadOsmData(stream, VehicleDefs.Bicycle);
            return;
        }

        if (profile == VehicleDefs.Pedestrian.Fastest())
        {
            routerDb.LoadOsmData(stream, VehicleDefs.Pedestrian);
            return;
        }

        // Default to car if the profile is unknown.
        routerDb.LoadOsmData(stream, VehicleDefs.Car);
    }
}
