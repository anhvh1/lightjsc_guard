using LightJSC.Workers.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LightJSC.Api.Health;

public sealed class ReadinessHealthCheck : IHealthCheck
{
    private readonly ReadinessState _state;

    public ReadinessHealthCheck(ReadinessState state)
    {
        _state = state;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_state.WatchlistLoaded && _state.RegistryRunning)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Watchlist loaded and registry running."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Waiting for watchlist or registry."));
    }
}

