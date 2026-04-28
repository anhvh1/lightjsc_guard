using System.Text.Json;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

/// <summary>
/// Manage runtime settings stored in the database.
/// </summary>
[ApiController]
[Route("api/v1/settings")]
public sealed class SettingsController : ControllerBase
{
    private const string AlarmSettingsKey = "alarm_delivery_settings";
    private const string MatchingSettingsKey = "matching_settings";
    private const string BestshotSettingsKey = "bestshot_settings";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRuntimeStateRepository _runtimeStateRepository;
    private readonly MatchingOptions _matchingOptions;
    private readonly BestshotOptions _bestshotOptions;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IRuntimeStateRepository runtimeStateRepository,
        IOptions<MatchingOptions> matchingOptions,
        IOptions<BestshotOptions> bestshotOptions,
        ILogger<SettingsController> logger)
    {
        _runtimeStateRepository = runtimeStateRepository;
        _matchingOptions = matchingOptions.Value;
        _bestshotOptions = bestshotOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Get alarm delivery settings.
    /// </summary>
    [HttpGet("alarm-delivery")]
    public async Task<ActionResult<AlarmDeliverySettings>> GetAlarmDelivery(CancellationToken cancellationToken)
    {
        var state = await _runtimeStateRepository.GetAsync(AlarmSettingsKey, cancellationToken);
        if (state is null || string.IsNullOrWhiteSpace(state.Value))
        {
            return Ok(AlarmDeliverySettings.Default);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AlarmDeliverySettings>(state.Value, SerializerOptions);
            return Ok(settings ?? AlarmDeliverySettings.Default);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Alarm delivery settings JSON is invalid.");
            return Ok(AlarmDeliverySettings.Default);
        }
    }

    /// <summary>
    /// Update alarm delivery settings.
    /// </summary>
    [HttpPut("alarm-delivery")]
    public async Task<ActionResult<AlarmDeliverySettings>> UpdateAlarmDelivery(
        [FromBody] AlarmDeliverySettings settings,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(settings ?? AlarmDeliverySettings.Default);
        await _runtimeStateRepository.UpsertAsync(new RuntimeState
        {
            Key = AlarmSettingsKey,
            Value = payload,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);

        return Ok(settings ?? AlarmDeliverySettings.Default);
    }

    /// <summary>
    /// Get matching threshold settings.
    /// </summary>
    [HttpGet("matching")]
    public async Task<ActionResult<MatchingSettings>> GetMatchingSettings(CancellationToken cancellationToken)
    {
        var defaults = BuildDefaultMatchingSettings();
        var state = await _runtimeStateRepository.GetAsync(MatchingSettingsKey, cancellationToken);
        if (state is null || string.IsNullOrWhiteSpace(state.Value))
        {
            return Ok(defaults);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<MatchingSettings>(state.Value, SerializerOptions);
            return Ok(settings ?? defaults);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Matching settings JSON is invalid.");
            return Ok(defaults);
        }
    }

    /// <summary>
    /// Update matching threshold settings.
    /// </summary>
    [HttpPut("matching")]
    public async Task<ActionResult<MatchingSettings>> UpdateMatchingSettings(
        [FromBody] MatchingSettings settings,
        CancellationToken cancellationToken)
    {
        settings ??= BuildDefaultMatchingSettings();
        if (!IsValidMatchingSettings(settings, out var error))
        {
            return BadRequest(error);
        }

        var payload = JsonSerializer.Serialize(settings);
        await _runtimeStateRepository.UpsertAsync(new RuntimeState
        {
            Key = MatchingSettingsKey,
            Value = payload,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);

        return Ok(settings);
    }

    /// <summary>
    /// Get bestshot storage settings.
    /// </summary>
    [HttpGet("bestshot")]
    public async Task<ActionResult<BestshotSettings>> GetBestshotSettings(CancellationToken cancellationToken)
    {
        var defaults = BuildDefaultBestshotSettings();
        var state = await _runtimeStateRepository.GetAsync(BestshotSettingsKey, cancellationToken);
        if (state is null || string.IsNullOrWhiteSpace(state.Value))
        {
            return Ok(defaults);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<BestshotSettings>(state.Value, SerializerOptions);
            return Ok(MergeBestshotSettings(settings, defaults));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Bestshot settings JSON is invalid.");
            return Ok(defaults);
        }
    }

    /// <summary>
    /// Update bestshot storage settings.
    /// </summary>
    [HttpPut("bestshot")]
    public async Task<ActionResult<BestshotSettings>> UpdateBestshotSettings(
        [FromBody] BestshotSettings settings,
        CancellationToken cancellationToken)
    {
        var defaults = BuildDefaultBestshotSettings();
        settings ??= defaults;
        if (!IsValidBestshotSettings(settings, out var error))
        {
            return BadRequest(error);
        }

        settings.RootPath = string.IsNullOrWhiteSpace(settings.RootPath)
            ? defaults.RootPath
            : settings.RootPath.Trim();

        var payload = JsonSerializer.Serialize(settings);
        await _runtimeStateRepository.UpsertAsync(new RuntimeState
        {
            Key = BestshotSettingsKey,
            Value = payload,
            UpdatedAt = DateTime.UtcNow
        }, cancellationToken);

        return Ok(settings);
    }

    private MatchingSettings BuildDefaultMatchingSettings()
    {
        return new MatchingSettings
        {
            Similarity = _matchingOptions.Similarity,
            Score = _matchingOptions.Score
        };
    }

    private BestshotSettings BuildDefaultBestshotSettings()
    {
        return new BestshotSettings
        {
            RootPath = _bestshotOptions.RootPath,
            RetentionDays = _bestshotOptions.RetentionDays
        };
    }

    private static BestshotSettings MergeBestshotSettings(BestshotSettings? settings, BestshotSettings defaults)
    {
        if (settings is null)
        {
            return defaults;
        }

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

    private static bool IsValidMatchingSettings(MatchingSettings settings, out string? error)
    {
        if (float.IsNaN(settings.Similarity) || float.IsInfinity(settings.Similarity))
        {
            error = "Similarity must be a valid number.";
            return false;
        }

        if (float.IsNaN(settings.Score) || float.IsInfinity(settings.Score))
        {
            error = "Score must be a valid number.";
            return false;
        }

        if (settings.Similarity < 0f || settings.Similarity > 1f)
        {
            error = "Similarity must be between 0 and 1.";
            return false;
        }

        if (settings.Score < 0f || settings.Score > 1f)
        {
            error = "Score must be between 0 and 1.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsValidBestshotSettings(BestshotSettings settings, out string? error)
    {
        if (settings.RetentionDays < 0)
        {
            error = "RetentionDays must be >= 0.";
            return false;
        }

        error = null;
        return true;
    }
}
