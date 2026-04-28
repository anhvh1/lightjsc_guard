using System.Text.Json;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Services;

public sealed class BestshotResolver
{
    private const string BestshotSettingsKey = "bestshot_settings";
    private readonly IRuntimeStateRepository _runtimeStateRepository;
    private readonly BestshotOptions _options;
    private readonly ILogger<BestshotResolver> _logger;
    private readonly string _contentRootPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly TimeSpan _settingsRefreshInterval = TimeSpan.FromSeconds(30);
    private BestshotSettings _settings;
    private DateTime _lastSettingsRefreshUtc = DateTime.MinValue;

    public BestshotResolver(
        IRuntimeStateRepository runtimeStateRepository,
        IOptions<BestshotOptions> options,
        IHostEnvironment environment,
        ILogger<BestshotResolver> logger)
    {
        _runtimeStateRepository = runtimeStateRepository;
        _options = options.Value;
        _logger = logger;
        _contentRootPath = environment.ContentRootPath;
        _settings = BuildDefaultSettings();
    }

    public async Task<string?> LoadBestshotBase64Async(string? relativePath, CancellationToken cancellationToken)
    {
        var bytes = await LoadBestshotBytesAsync(relativePath, cancellationToken);
        return bytes is { Length: > 0 } ? Convert.ToBase64String(bytes) : null;
    }

    public async Task<byte[]?> LoadBestshotBytesAsync(string? relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var settings = await GetBestshotSettingsAsync(cancellationToken);
        if (!TryResolveBestshotPath(relativePath, settings, out var fullPath))
        {
            return null;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read bestshot file {Path}", fullPath);
            return null;
        }
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
            return _settings;
        }

        await _settingsLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTime.UtcNow - _lastSettingsRefreshUtc < _settingsRefreshInterval)
            {
                return _settings;
            }

            var settings = BuildDefaultSettings();
            var state = await _runtimeStateRepository.GetAsync(BestshotSettingsKey, cancellationToken);
            if (state is not null && !string.IsNullOrWhiteSpace(state.Value))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<BestshotSettings>(state.Value, _serializerOptions);
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

            _settings = settings;
            _lastSettingsRefreshUtc = DateTime.UtcNow;
            return _settings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private bool TryResolveBestshotPath(string relativePath, BestshotSettings settings, out string fullPath)
    {
        fullPath = string.Empty;
        var rootPath = ResolveRootPath(_contentRootPath, settings.RootPath);
        var resolved = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var rootFullPath = Path.GetFullPath(rootPath);
        if (!resolved.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Bestshot path traversal attempt blocked: {Path}", resolved);
            return false;
        }

        fullPath = resolved;
        return true;
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
}
