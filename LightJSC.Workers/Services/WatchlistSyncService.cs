using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Workers.Health;
using LightJSC.Workers.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightJSC.Workers.Services;

public sealed class WatchlistSyncService : BackgroundService
{
    private const string LastSyncKey = "watchlist_last_sync";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVectorIndex _vectorIndex;
    private readonly WatchlistOptions _options;
    private readonly ReadinessState _readinessState;
    private readonly ILogger<WatchlistSyncService> _logger;
    private readonly HashSet<string> _currentIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private DateTime _lastFullRefreshUtc;

    public WatchlistSyncService(
        IServiceScopeFactory scopeFactory,
        IVectorIndex vectorIndex,
        IOptions<WatchlistOptions> options,
        ReadinessState readinessState,
        ILogger<WatchlistSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _vectorIndex = vectorIndex;
        _options = options.Value;
        _readinessState = readinessState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await FullRefreshAsync(stoppingToken);
            _readinessState.MarkWatchlistLoaded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watchlist full refresh failed.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.SyncIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IncrementalSyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchlist incremental sync failed.");
            }

            if (DateTime.UtcNow - _lastFullRefreshUtc >= TimeSpan.FromMinutes(_options.FullRefreshMinutes))
            {
                try
                {
                    await FullRefreshAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Watchlist full refresh failed.");
                }
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task IncrementalSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var watchlistRepository = scope.ServiceProvider.GetRequiredService<IWatchlistRepository>();
        var runtimeStateRepository = scope.ServiceProvider.GetRequiredService<IRuntimeStateRepository>();

        var lastSync = await GetLastSyncAsync(runtimeStateRepository, cancellationToken);
        var updates = await watchlistRepository.FetchUpdatedAsync(lastSync, cancellationToken);
        if (updates.Count == 0)
        {
            return;
        }

        foreach (var entry in updates)
        {
            ApplyEntry(entry);
        }

        await SaveLastSyncAsync(runtimeStateRepository, DateTime.UtcNow, cancellationToken);
        MetricsRegistry.WatchlistSize.Set(_vectorIndex.Count);
        _logger.LogInformation("Watchlist incremental sync applied {Count} entries", updates.Count);
    }

    private async Task FullRefreshAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var watchlistRepository = scope.ServiceProvider.GetRequiredService<IWatchlistRepository>();
        var runtimeStateRepository = scope.ServiceProvider.GetRequiredService<IRuntimeStateRepository>();

        var entries = await watchlistRepository.FetchAllActiveAsync(cancellationToken);
        var newIds = new HashSet<string>(entries.Select(e => e.EntryId), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            ApplyEntry(entry);
        }

        lock (_lock)
        {
            foreach (var existing in _currentIds.Except(newIds, StringComparer.OrdinalIgnoreCase).ToList())
            {
                _vectorIndex.Remove(existing);
                _currentIds.Remove(existing);
            }

            foreach (var id in newIds)
            {
                _currentIds.Add(id);
            }
        }

        _lastFullRefreshUtc = DateTime.UtcNow;
        await SaveLastSyncAsync(runtimeStateRepository, DateTime.UtcNow, cancellationToken);
        MetricsRegistry.WatchlistSize.Set(_vectorIndex.Count);
        _logger.LogInformation("Watchlist full refresh loaded {Count} entries", entries.Count);
    }

    private void ApplyEntry(WatchlistEntry entry)
    {
        if (!entry.IsActive || entry.FeatureVector.Length == 0)
        {
            _vectorIndex.Remove(entry.EntryId);
            lock (_lock)
            {
                _currentIds.Remove(entry.EntryId);
            }
            return;
        }

        _vectorIndex.AddOrUpdate(entry);
        lock (_lock)
        {
            _currentIds.Add(entry.EntryId);
        }
    }

    private static async Task<DateTime> GetLastSyncAsync(IRuntimeStateRepository runtimeStateRepository, CancellationToken cancellationToken)
    {
        var state = await runtimeStateRepository.GetAsync(LastSyncKey, cancellationToken);
        if (state is null)
        {
            return DateTime.UtcNow.AddMinutes(-10);
        }

        if (DateTime.TryParse(state.Value, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTime.UtcNow.AddMinutes(-10);
    }

    private static Task SaveLastSyncAsync(IRuntimeStateRepository runtimeStateRepository, DateTime value, CancellationToken cancellationToken)
    {
        return runtimeStateRepository.UpsertAsync(new RuntimeState
        {
            Key = LastSyncKey,
            Value = value.ToUniversalTime().ToString("O"),
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);
    }
}

