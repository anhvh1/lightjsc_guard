using System.Runtime.InteropServices;
using System.Text.Json;
using LightJSC.Api.Contracts;
using LightJSC.Core.Helpers;
using LightJSC.Api.Services;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Data.Repositories;
using LightJSC.Infrastructure.Enrollment;
using LightJSC.Infrastructure.Vector;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/face-trace")]
public sealed class FaceTraceController : ControllerBase
{
    private readonly FaceEventSearchRepository _searchRepository;
    private readonly IFaceTemplateRepository _templateRepository;
    private readonly IPersonRepository _personRepository;
    private readonly ICameraRepository _cameraRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly IFaceEnrollmentClient _enrollmentClient;
    private readonly EnrollmentOptions _enrollmentOptions;
    private readonly BestshotResolver _bestshotResolver;
    private readonly ILogger<FaceTraceController> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public FaceTraceController(
        FaceEventSearchRepository searchRepository,
        IFaceTemplateRepository templateRepository,
        IPersonRepository personRepository,
        ICameraRepository cameraRepository,
        ISecretProtector secretProtector,
        IFaceEnrollmentClient enrollmentClient,
        IOptions<EnrollmentOptions> enrollmentOptions,
        BestshotResolver bestshotResolver,
        ILogger<FaceTraceController> logger)
    {
        _searchRepository = searchRepository;
        _templateRepository = templateRepository;
        _personRepository = personRepository;
        _cameraRepository = cameraRepository;
        _secretProtector = secretProtector;
        _enrollmentClient = enrollmentClient;
        _enrollmentOptions = enrollmentOptions.Value;
        _bestshotResolver = bestshotResolver;
        _logger = logger;
    }

    [HttpPost("person")]
    public async Task<ActionResult<FaceEventSearchResponse>> TraceByPerson(
        [FromBody] FaceTracePersonRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new FaceTracePersonRequest();
        if (request.PersonId == Guid.Empty)
        {
            return BadRequest("PersonId is required.");
        }

        var person = await _personRepository.GetAsync(request.PersonId, cancellationToken);
        if (person is null)
        {
            return NotFound("Person not found.");
        }

        var templates = await _templateRepository.ListByPersonAsync(request.PersonId, cancellationToken);
        var activeTemplates = templates
            .Where(template => template.IsActive && template.FeatureBytes.Length > 0)
            .ToList();
        if (activeTemplates.Count == 0)
        {
            return Ok(new FaceEventSearchResponse
            {
                Items = Array.Empty<FaceEventResponse>(),
                Total = 0
            });
        }

        var topK = ClampTopK(request.TopK);
        var similarityMin = NormalizeTraceSimilarity(request.SimilarityMin);
        var perTemplateTopK = ResolvePerTemplateTopK(topK, activeTemplates.Count);
        var filter = BuildTraceFilter(request.Filter);
        if (request.Filter?.WatchlistEntryIds is null || request.Filter.WatchlistEntryIds.Count == 0)
        {
            var watchlistIds = activeTemplates
                .Select(template => template.Id.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (watchlistIds.Length > 0)
            {
                filter = new FaceEventSearchFilter
                {
                    FromUtc = filter.FromUtc,
                    ToUtc = filter.ToUtc,
                    CameraIds = filter.CameraIds,
                    IsKnown = filter.IsKnown,
                    ListType = filter.ListType,
                    Gender = filter.Gender,
                    AgeMin = filter.AgeMin,
                    AgeMax = filter.AgeMax,
                    Mask = filter.Mask,
                    ScoreMin = filter.ScoreMin,
                    SimilarityMin = filter.SimilarityMin,
                    PersonQuery = filter.PersonQuery,
                    Category = filter.Category,
                    HasFeature = filter.HasFeature,
                    WatchlistEntryIds = watchlistIds,
                    Page = filter.Page,
                    PageSize = filter.PageSize
                };
            }
        }

        var merged = new Dictionary<Guid, FaceEventSearchRow>();
        foreach (var template in activeTemplates)
        {
            if (template.FeatureBytes.Length % sizeof(float) != 0)
            {
                continue;
            }

            var vector = MemoryMarshal.Cast<byte, float>(template.FeatureBytes).ToArray();
            if (vector.Length == 0)
            {
                continue;
            }

            VectorMath.NormalizeInPlace(vector);
            var featureVersion = ResolveTemplateFeatureVersion(template.FeatureVersion);
            var result = await _searchRepository.SearchSimilarAsync(
                vector,
                featureVersion,
                perTemplateTopK,
                filter,
                cancellationToken);
            foreach (var row in result.Items)
            {
                if (!merged.TryGetValue(row.Id, out var existing))
                {
                    merged[row.Id] = row;
                    continue;
                }

                var existingScore = existing.TraceSimilarity ?? 0f;
                var currentScore = row.TraceSimilarity ?? 0f;
                if (currentScore > existingScore)
                {
                    merged[row.Id] = row;
                }
            }
        }

        IEnumerable<FaceEventSearchRow> items = merged.Values;
        if (similarityMin.HasValue)
        {
            items = items.Where(row => (row.TraceSimilarity ?? 0f) >= similarityMin.Value);
        }

        var ordered = items
            .OrderByDescending(row => row.TraceSimilarity ?? 0f)
            .ThenByDescending(row => row.EventTimeUtc)
            .Take(topK)
            .ToList();

        var response = await BuildSearchResponseAsync(ordered, ordered.Count, request.IncludeBestshot, cancellationToken);
        return Ok(response);
    }

    [HttpPost("image")]
    public async Task<ActionResult<FaceEventSearchResponse>> TraceByImage(
        [FromBody] FaceTraceImageRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new FaceTraceImageRequest();
        if (string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            return BadRequest("ImageBase64 is required.");
        }

        if (!TryDecodeBase64(request.ImageBase64, out var imageBytes))
        {
            return BadRequest("ImageBase64 is invalid.");
        }

        var cameras = await _cameraRepository.ListAsync(cancellationToken);
        var enabledCameras = cameras.Where(camera => camera.Enabled).ToList();
        if (enabledCameras.Count == 0)
        {
            return BadRequest("No cameras available for trace.");
        }

        CameraCredential? preferred = null;
        if (!string.IsNullOrWhiteSpace(request.CameraId))
        {
            preferred = enabledCameras.FirstOrDefault(camera =>
                string.Equals(camera.CameraId, request.CameraId, StringComparison.OrdinalIgnoreCase));
            if (preferred is null)
            {
                return NotFound("Camera not found.");
            }
        }

        var topK = ClampTopK(request.TopK);
        var similarityMin = NormalizeTraceSimilarity(request.SimilarityMin);
        var filter = BuildTraceFilter(request.Filter);

        var selectedCameras = SelectSeriesCameras(enabledCameras, preferred);
        if (selectedCameras.Count == 0)
        {
            return BadRequest("No cameras available for trace.");
        }

        var perCameraTopK = ResolvePerCameraTopK(topK, selectedCameras.Count);
        var merged = new Dictionary<Guid, FaceEventSearchRow>();
        foreach (var camera in selectedCameras)
        {
            var password = string.IsNullOrWhiteSpace(camera.RtspPasswordEncrypted)
                ? string.Empty
                : _secretProtector.DecryptFromBase64(camera.RtspPasswordEncrypted);

            FaceEnrollmentResult enrollment;
            try
            {
                enrollment = await _enrollmentClient.EnrollAsync(
                    camera.IpAddress,
                    camera.RtspUsername,
                    password,
                    imageBytes,
                    cancellationToken);
            }
            catch (EnrollmentTemplateException ex)
            {
                _logger.LogWarning(ex, "Enrollment template missing for camera {CameraId} ({CameraIp}).", camera.CameraId, camera.IpAddress);
                return BadRequest(ex.Message);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Enrollment timeout for camera {CameraId} ({CameraIp}).", camera.CameraId, camera.IpAddress);
                continue;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Enrollment request failed for camera {CameraId} ({CameraIp}).", camera.CameraId, camera.IpAddress);
                continue;
            }

            if (enrollment.FeatureVector.Length == 0)
            {
                continue;
            }

            var featureVersion = FeatureVersionHelper.Combine(_enrollmentOptions.FeatureVersion, camera.CameraSeries);
            var result = await _searchRepository.SearchSimilarAsync(
                enrollment.FeatureVector,
                featureVersion,
                perCameraTopK,
                filter,
                cancellationToken);

            foreach (var row in result.Items)
            {
                if (!merged.TryGetValue(row.Id, out var existing))
                {
                    merged[row.Id] = row;
                    continue;
                }

                var existingScore = existing.TraceSimilarity ?? 0f;
                var currentScore = row.TraceSimilarity ?? 0f;
                if (currentScore > existingScore)
                {
                    merged[row.Id] = row;
                }
            }
        }

        IEnumerable<FaceEventSearchRow> items = merged.Values;
        if (similarityMin.HasValue)
        {
            items = items.Where(row => (row.TraceSimilarity ?? 0f) >= similarityMin.Value);
        }

        var ordered = items
            .OrderByDescending(row => row.TraceSimilarity ?? 0f)
            .ThenByDescending(row => row.EventTimeUtc)
            .Take(topK)
            .ToList();

        var response = await BuildSearchResponseAsync(ordered, ordered.Count, request.IncludeBestshot, cancellationToken);
        return Ok(response);
    }

    [HttpPost("event/{eventId:guid}")]
    public async Task<ActionResult<FaceEventSearchResponse>> TraceByEvent(
        Guid eventId,
        [FromBody] FaceTraceEventRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new FaceTraceEventRequest();
        var topK = ClampTopK(request.TopK);
        var similarityMin = NormalizeTraceSimilarity(request.SimilarityMin);
        var filter = BuildTraceFilter(request.Filter);
        var result = await _searchRepository.SearchSimilarToEventAsync(eventId, topK, filter, cancellationToken);
        var filtered = similarityMin.HasValue
            ? result.Items.Where(row => (row.TraceSimilarity ?? 0f) >= similarityMin.Value).ToList()
            : result.Items;
        var response = await BuildSearchResponseAsync(filtered, filtered.Count, request.IncludeBestshot, cancellationToken);
        return Ok(response);
    }

    private static FaceEventSearchFilter BuildTraceFilter(FaceEventSearchFilterRequest? request)
    {
        return new FaceEventSearchFilter
        {
            FromUtc = request?.FromUtc,
            ToUtc = request?.ToUtc,
            CameraIds = request?.CameraIds,
            IsKnown = request?.IsKnown,
            ListType = request?.ListType,
            Gender = request?.Gender,
            AgeMin = request?.AgeMin,
            AgeMax = request?.AgeMax,
            Mask = request?.Mask,
            ScoreMin = request?.ScoreMin,
            SimilarityMin = request?.SimilarityMin,
            PersonQuery = request?.PersonQuery,
            Category = request?.Category,
            HasFeature = true,
            WatchlistEntryIds = request?.WatchlistEntryIds,
            Page = 1,
            PageSize = 1
        };
    }


    private async Task<FaceEventSearchResponse> BuildSearchResponseAsync(
        IReadOnlyList<FaceEventSearchRow> rows,
        int total,
        bool includeBestshot,
        CancellationToken cancellationToken)
    {
        var items = await BuildResponsesAsync(rows, includeBestshot, cancellationToken);
        return new FaceEventSearchResponse
        {
            Items = items,
            Total = total
        };
    }

    private async Task<IReadOnlyList<FaceEventResponse>> BuildResponsesAsync(
        IReadOnlyList<FaceEventSearchRow> rows,
        bool includeBestshot,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<FaceEventResponse>();
        }

        if (!includeBestshot)
        {
            return rows.Select(row => MapRow(row, null)).ToList();
        }

        var tasks = rows.Select(async row =>
        {
            var bestshot = await _bestshotResolver.LoadBestshotBase64Async(row.BestshotPath, cancellationToken);
            return MapRow(row, bestshot);
        }).ToList();

        return await Task.WhenAll(tasks);
    }

    private FaceEventResponse MapRow(FaceEventSearchRow row, string? bestshotBase64)
    {
        return new FaceEventResponse
        {
            Id = row.Id,
            EventTimeUtc = row.EventTimeUtc,
            CameraId = row.CameraId,
            CameraIp = row.CameraIp,
            CameraZone = row.CameraZone,
            IsKnown = row.IsKnown,
            WatchlistEntryId = row.WatchlistEntryId,
            PersonId = row.PersonId,
            Person = DeserializePerson(row.PersonJson),
            Similarity = row.WatchlistSimilarity,
            Score = row.Score,
            BestshotBase64 = bestshotBase64,
            Gender = row.Gender,
            Age = row.Age,
            Mask = row.Mask,
            HasFeature = row.HasFeature,
            TraceSimilarity = row.TraceSimilarity
        };
    }

    private PersonProfile? DeserializePerson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PersonProfile>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize person payload for face event.");
            return null;
        }
    }

    private static int ClampTopK(int topK)
    {
        var resolved = topK <= 0 ? 50 : topK;
        return Math.Clamp(resolved, 1, 200);
    }

    private static int ResolvePerTemplateTopK(int topK, int templateCount)
    {
        if (templateCount <= 1)
        {
            return ClampTopK(topK);
        }

        var expanded = topK * 2;
        return Math.Clamp(expanded, 10, 200);
    }

    private static int ResolvePerCameraTopK(int topK, int cameraCount)
    {
        if (cameraCount <= 1)
        {
            return ClampTopK(topK);
        }

        var expanded = topK * 2;
        return Math.Clamp(expanded, 10, 200);
    }

    private static float? NormalizeTraceSimilarity(float? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = value.Value;
        if (float.IsNaN(normalized) || float.IsInfinity(normalized))
        {
            return null;
        }

        return Math.Clamp(normalized, 0f, 1f);
    }

    private string ResolveTemplateFeatureVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return _enrollmentOptions.FeatureVersion;
        }

        if (!string.IsNullOrWhiteSpace(_enrollmentOptions.AppName)
            && string.Equals(version, _enrollmentOptions.AppName, StringComparison.OrdinalIgnoreCase))
        {
            return _enrollmentOptions.FeatureVersion;
        }

        return version.Trim();
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

    private static IReadOnlyList<CameraCredential> SelectSeriesCameras(
        IReadOnlyCollection<CameraCredential> cameras,
        CameraCredential? preferred)
    {
        var groups = cameras
            .GroupBy(camera => FeatureVersionHelper.NormalizeSeries(camera.CameraSeries), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        var selected = new List<CameraCredential>();
        foreach (var group in groups)
        {
            var chosen = preferred is not null
                ? group.FirstOrDefault(camera => camera.CameraId == preferred.CameraId)
                : null;
            chosen ??= group.OrderBy(camera => camera.CameraId, StringComparer.OrdinalIgnoreCase).First();
            selected.Add(chosen);
        }

        return selected;
    }
}
