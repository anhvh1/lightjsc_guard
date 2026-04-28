using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using System.Linq;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightJSC.Workers.Services;

public sealed class BestshotStorageService
{
    private const string BestshotSettingsKey = "bestshot_settings";
    private readonly BestshotOptions _options;
    private readonly IFaceEventIndex _eventIndex;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BestshotStorageService> _logger;
    private readonly string _contentRootPath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly JsonSerializerOptions _settingsSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly TimeSpan _settingsRefreshInterval = TimeSpan.FromSeconds(30);
    private BestshotSettings _bestshotSettings;
    private DateTime _lastSettingsRefreshUtc = DateTime.MinValue;

    public BestshotStorageService(
        IOptions<BestshotOptions> options,
        IFaceEventIndex eventIndex,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<BestshotStorageService> logger)
    {
        _options = options.Value;
        _eventIndex = eventIndex;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _contentRootPath = environment.ContentRootPath;
        _bestshotSettings = BuildDefaultSettings();
    }

    public async Task StoreAsync(FaceMatchDecision decision, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (decision.IsKnown && !_options.StoreKnown)
        {
            return;
        }

        if (!decision.IsKnown && !_options.StoreUnknown)
        {
            return;
        }

        var faceEvent = decision.FaceEvent;
        if (!TryDecodeBase64(faceEvent.FaceImageBase64, out var imageBytes))
        {
            return;
        }

        if (_options.MaxBytes > 0 && imageBytes.Length > _options.MaxBytes)
        {
            if (TryCompressBestshot(imageBytes, _options.MaxBytes, out var compressed))
            {
                imageBytes = compressed;
            }
        }

        var settings = await GetBestshotSettingsAsync(cancellationToken);
        var rootPath = ResolveRootPath(_contentRootPath, settings.RootPath);
        var relativePath = BuildRelativePath(faceEvent, imageBytes);
        var fullPath = Path.Combine(rootPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            await File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write bestshot file {Path}", fullPath);
            return;
        }

        var record = new FaceEventRecord
        {
            Id = Guid.NewGuid(),
            EventTimeUtc = faceEvent.EventTimeUtc.UtcDateTime,
            CameraId = faceEvent.CameraId,
            IsKnown = decision.IsKnown,
            WatchlistEntryId = decision.WatchlistEntryId,
            PersonId = decision.PersonId,
            PersonJson = decision.Person is null ? null : JsonSerializer.Serialize(decision.Person, _jsonOptions),
            Similarity = decision.Similarity,
            Score = faceEvent.Score,
            BestshotPath = relativePath,
            ThumbPath = null,
            Gender = faceEvent.Gender,
            Age = faceEvent.Age,
            Mask = faceEvent.Mask,
            BBoxJson = faceEvent.BBox is null ? null : JsonSerializer.Serialize(faceEvent.BBox, _jsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IFaceEventRepository>();
            await repository.AddAsync(record, cancellationToken);

            try
            {
                await _eventIndex.UpsertAsync(new FaceEventIndexEntry
                {
                    EventId = record.Id,
                    EventTimeUtc = record.EventTimeUtc,
                    CameraId = record.CameraId,
                    FeatureVersion = faceEvent.FeatureVersion,
                    FeatureVector = faceEvent.FeatureVector
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index face event {EventId}", record.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store face event record for {CameraId}", faceEvent.CameraId);
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

    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var marker = "base64,";
        var markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            trimmed = trimmed[(markerIndex + marker.Length)..];
        }

        try
        {
            bytes = Convert.FromBase64String(trimmed);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string BuildRelativePath(FaceEvent faceEvent, byte[] imageBytes)
    {
        var eventTime = faceEvent.EventTimeUtc.ToUniversalTime();
        var cameraSegment = SanitizeSegment(faceEvent.CameraId);
        var dateSegment = Path.Combine(eventTime.ToString("yyyy"), eventTime.ToString("MM"), eventTime.ToString("dd"));
        var hash = faceEvent.FeatureBytes.Length > 0
            ? Convert.ToHexString(SHA256.HashData(faceEvent.FeatureBytes))
            : Convert.ToHexString(SHA256.HashData(imageBytes));
        var fileName = $"{eventTime:yyyyMMdd_HHmmssfff}_{hash}.jpg";
        return Path.Combine(dateSegment, cameraSegment, fileName);
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
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

#pragma warning disable CA1416
    private bool TryCompressBestshot(byte[] sourceBytes, int maxBytes, out byte[] outputBytes)
    {
        outputBytes = sourceBytes;
        if (sourceBytes.Length <= maxBytes)
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(sourceBytes);
            using var original = Image.FromStream(input);
            var encoder = GetJpegEncoder();
            if (encoder is null)
            {
                return false;
            }

            byte[]? smallest = null;
            var width = original.Width;
            var height = original.Height;
            var minDimension = 32;
            var firstPass = true;
            var qualitySteps = new long[] { 80, 70, 60, 50, 40, 30, 25 };

            while (firstPass || (width >= minDimension && height >= minDimension))
            {
                firstPass = false;
                using var candidate = width == original.Width && height == original.Height
                    ? new Bitmap(original)
                    : ResizeImage(original, width, height);

                foreach (var quality in qualitySteps)
                {
                    var encoded = EncodeJpeg(candidate, encoder, quality);
                    if (encoded.Length <= maxBytes)
                    {
                        outputBytes = encoded;
                        return true;
                    }

                    if (smallest is null || encoded.Length < smallest.Length)
                    {
                        smallest = encoded;
                    }
                }

                width = Math.Max(1, (int)Math.Round(width * 0.85));
                height = Math.Max(1, (int)Math.Round(height * 0.85));
            }

            if (smallest is not null && smallest.Length < sourceBytes.Length)
            {
                outputBytes = smallest;
                _logger.LogWarning(
                    "Bestshot still above max bytes after compression. size={Size} max={Max}",
                    outputBytes.Length,
                    maxBytes);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compress bestshot image.");
        }

        return false;
    }

    private static ImageCodecInfo? GetJpegEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    private static byte[] EncodeJpeg(Image image, ImageCodecInfo encoder, long quality)
    {
        using var output = new MemoryStream();
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        image.Save(output, encoder, encoderParams);
        return output.ToArray();
    }

    private static Bitmap ResizeImage(Image image, int width, int height)
    {
        var dest = new Bitmap(width, height);
        dest.SetResolution(image.HorizontalResolution, image.VerticalResolution);
        using var graphics = Graphics.FromImage(dest);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var wrapMode = new ImageAttributes();
        wrapMode.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(
            image,
            new Rectangle(0, 0, width, height),
            0,
            0,
            image.Width,
            image.Height,
            GraphicsUnit.Pixel,
            wrapMode);
        return dest;
    }
#pragma warning restore CA1416
}
