using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace LightJSC.Workers.Services;

public sealed class BestshotRetentionService : BackgroundService
{
    private const int BatchSize = 500;
    private const string BestshotSettingsKey = "bestshot_settings";
    private readonly BestshotOptions _options;
    private readonly IFaceEventIndex _eventIndex;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BestshotRetentionService> _logger;
    private readonly string _contentRootPath;
    private readonly JsonSerializerOptions _settingsSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly TimeSpan _settingsRefreshInterval = TimeSpan.FromSeconds(30);
    private BestshotSettings _bestshotSettings;
    private DateTime _lastSettingsRefreshUtc = DateTime.MinValue;

    public BestshotRetentionService(
        IOptions<BestshotOptions> options,
        IFaceEventIndex eventIndex,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<BestshotRetentionService> logger)
    {
        _options = options.Value;
        _eventIndex = eventIndex;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _contentRootPath = environment.ContentRootPath;
        _bestshotSettings = BuildDefaultSettings();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _options.RetentionDays <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _options.CleanupIntervalHours));
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await GetBestshotSettingsAsync(stoppingToken);
                if (settings.RetentionDays > 0)
                {
                    await CleanupOnceAsync(settings, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bestshot retention cleanup failed.");
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

    private async Task CleanupOnceAsync(BestshotSettings settings, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-settings.RetentionDays);
        var rootPath = ResolveRootPath(_contentRootPath, settings.RootPath);
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IFaceEventRepository>();

        while (true)
        {
            var records = await repository.ListOlderThanAsync(cutoff, BatchSize, cancellationToken);
            if (records.Count == 0)
            {
                break;
            }

            foreach (var record in records)
            {
                TryDeleteFile(rootPath, record.BestshotPath);
                TryDeleteFile(rootPath, record.ThumbPath);
            }

            var ids = records.Select(record => record.Id).ToList();
            await repository.DeleteByIdsAsync(ids, cancellationToken);
            await _eventIndex.DeleteByEventIdsAsync(ids, cancellationToken);
        }
    }

    private void TryDeleteFile(string rootPath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var fullPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(rootPath, relativePath);

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete bestshot file {Path}", fullPath);
        }
    }

    private static string ResolveRootPath(string contentRootPath, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return contentRootPath;
        }

        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(contentRootPath, path);
    }

    private BestshotSettings BuildDefaultSettings()
    {
        return new BestshotSettings
        {
            RootPath = _options.RootPath,
            RetentionDays = _options.RetentionDays
        };
    }

    private async Task<BestshotSettings> GetBestshotSettingsAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastSettingsRefreshUtc < _settingsRefreshInterval)
        {
            return _bestshotSettings;
        }

        await _settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _lastSettingsRefreshUtc < _settingsRefreshInterval)
            {
                return _bestshotSettings;
            }

            var settings = BuildDefaultSettings();
            using var scope = _scopeFactory.CreateScope();
            var runtimeStateRepository = scope.ServiceProvider.GetRequiredService<IRuntimeStateRepository>();
            var state = await runtimeStateRepository.GetAsync(BestshotSettingsKey, cancellationToken);
            if (state is not null && !string.IsNullOrWhiteSpace(state.Value))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<BestshotSettings>(state.Value, _settingsSerializerOptions);
                    if (parsed is not null)
                    {
                        settings = MergeSettings(parsed, settings);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Bestshot settings JSON is invalid.");
                }
            }

            _bestshotSettings = settings;
            _lastSettingsRefreshUtc = DateTime.UtcNow;
            return _bestshotSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private static BestshotSettings MergeSettings(BestshotSettings settings, BestshotSettings defaults)
    {
        var rootPath = string.IsNullOrWhiteSpace(settings.RootPath)
            ? defaults.RootPath
            : settings.RootPath.Trim();

        var retentionDays = settings.RetentionDays < 0
            ? defaults.RetentionDays
            : settings.RetentionDays;

        return new BestshotSettings
        {
            RootPath = rootPath,
            RetentionDays = retentionDays
        };
    }
}
