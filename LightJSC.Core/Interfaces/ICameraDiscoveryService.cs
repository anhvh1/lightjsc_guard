using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface ICameraDiscoveryService
{
    Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(
        TimeSpan timeout,
        System.Net.IPAddress? ipStart,
        System.Net.IPAddress? ipEnd,
        CancellationToken cancellationToken);
}
