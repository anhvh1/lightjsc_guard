using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IRoutingService
{
    Task<RouteResult> BuildRouteAsync(IReadOnlyList<GeoPoint> points, string? mode, CancellationToken cancellationToken);
}

public sealed class RouteResult
{
    public List<GeoPoint> Points { get; init; } = new();
    public bool IsFallback { get; init; }
}
