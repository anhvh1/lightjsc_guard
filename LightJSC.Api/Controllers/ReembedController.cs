using System.Security.Cryptography;
using LightJSC.Api.Contracts;
using LightJSC.Core.Helpers;
using LightJSC.Api.Services;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Enrollment;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/re-embed")]
public sealed class ReembedController : ControllerBase
{
    private const int MaxErrors = 20;
    private readonly IPersonRepository _personRepository;
    private readonly IFaceTemplateRepository _templateRepository;
    private readonly IFaceEventRepository _faceEventRepository;
    private readonly ICameraRepository _cameraRepository;
    private readonly IFaceEventIndex _faceEventIndex;
    private readonly ISecretProtector _secretProtector;
    private readonly IFaceEnrollmentClient _enrollmentClient;
    private readonly BestshotResolver _bestshotResolver;
    private readonly EnrollmentOptions _enrollmentOptions;
    private readonly ILogger<ReembedController> _logger;

    public ReembedController(
        IPersonRepository personRepository,
        IFaceTemplateRepository templateRepository,
        IFaceEventRepository faceEventRepository,
        ICameraRepository cameraRepository,
        IFaceEventIndex faceEventIndex,
        ISecretProtector secretProtector,
        IFaceEnrollmentClient enrollmentClient,
        BestshotResolver bestshotResolver,
        IOptions<EnrollmentOptions> enrollmentOptions,
        ILogger<ReembedController> logger)
    {
        _personRepository = personRepository;
        _templateRepository = templateRepository;
        _faceEventRepository = faceEventRepository;
        _cameraRepository = cameraRepository;
        _faceEventIndex = faceEventIndex;
        _secretProtector = secretProtector;
        _enrollmentClient = enrollmentClient;
        _bestshotResolver = bestshotResolver;
        _enrollmentOptions = enrollmentOptions.Value;
        _logger = logger;
    }

    [HttpPost("persons")]
    public async Task<ActionResult<ReembedResult>> ReembedPersons(
        [FromBody] ReembedPersonsRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new ReembedPersonsRequest();
        var cameras = await ResolveCamerasAsync(request.CameraId, request.CameraIds, cancellationToken);
        if (cameras.Count == 0)
        {
            return BadRequest("No cameras found for re-embed.");
        }

        var featureVersionByCamera = NormalizeFeatureVersionMap(request.FeatureVersionByCamera);
        var passwordByCameraId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var camera in cameras)
        {
            passwordByCameraId[camera.CameraId] = string.IsNullOrWhiteSpace(camera.RtspPasswordEncrypted)
                ? string.Empty
                : _secretProtector.DecryptFromBase64(camera.RtspPasswordEncrypted);
        }

        var maxPersons = ClampLimit(request.MaxPersons, 1, 1000);
        var errors = new List<string>();
        var result = new ReembedResult();

        var persons = await LoadPersonsAsync(request.PersonIds, cancellationToken);
        if (!request.IncludeInactive)
        {
            persons = persons.Where(person => person.IsActive).ToList();
        }

        foreach (var person in persons.Take(maxPersons))
        {
            var templates = await _templateRepository.ListByPersonAsync(person.Id, cancellationToken);
            foreach (var camera in cameras)
            {
                var password = passwordByCameraId[camera.CameraId];
                var targetFeatureVersion = ResolveTargetFeatureVersion(
                    request.TargetFeatureVersion,
                    featureVersionByCamera,
                    camera.CameraId);
                targetFeatureVersion = FeatureVersionHelper.Combine(targetFeatureVersion, camera.CameraSeries);

                foreach (var template in templates)
                {
                    if (!request.IncludeInactive && !template.IsActive)
                    {
                        continue;
                    }

                    if (template.FaceImageJpeg is null || template.FaceImageJpeg.Length == 0)
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (IsSameFeatureVersion(template.FeatureVersion, targetFeatureVersion))
                    {
                        result.Skipped++;
                        continue;
                    }

                    result.Processed++;
                    if (request.DryRun)
                    {
                        result.Created++;
                        continue;
                    }

                    try
                    {
                        var enrollment = await _enrollmentClient.EnrollAsync(
                            camera.IpAddress,
                            camera.RtspUsername,
                            password,
                            template.FaceImageJpeg,
                            cancellationToken);

                        if (enrollment.FeatureBytes.Length == 0)
                        {
                            result.Failed++;
                            AddError(errors, $"person {person.Id} template {template.Id}: empty feature");
                            continue;
                        }

                        var featureHash = Convert.ToHexString(SHA256.HashData(enrollment.FeatureBytes));
                        if (await _templateRepository.ExistsByHashAsync(person.Id, featureHash, cancellationToken))
                        {
                            result.Skipped++;
                            continue;
                        }

                        var now = DateTime.UtcNow;
                        var newTemplate = new FaceTemplate
                        {
                            Id = Guid.NewGuid(),
                            PersonId = person.Id,
                            FeatureBytes = enrollment.FeatureBytes,
                            L2Norm = enrollment.L2Norm,
                            FeatureVersion = targetFeatureVersion,
                            FaceImageJpeg = template.FaceImageJpeg,
                            SourceCameraId = camera.CameraId,
                            FeatureHash = featureHash,
                            IsActive = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        };

                        await _templateRepository.AddAsync(newTemplate, cancellationToken);
                        result.Created++;
                    }
                    catch (EnrollmentTemplateException ex)
                    {
                        _logger.LogWarning(ex, "Re-embed template missing for camera {CameraId}", camera.CameraId);
                        result.Failed++;
                        AddError(errors, $"person {person.Id} template {template.Id}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Re-embed failed for person {PersonId} template {TemplateId}", person.Id, template.Id);
                        result.Failed++;
                        AddError(errors, $"person {person.Id} template {template.Id}: {ex.Message}");
                    }
                }
            }
        }

        result.Errors = errors;
        return Ok(result);
    }

    [HttpPost("events")]
    public async Task<ActionResult<ReembedResult>> ReembedEvents(
        [FromBody] ReembedEventsRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new ReembedEventsRequest();
        var cameras = await ResolveCamerasAsync(request.CameraId, request.CameraIds, cancellationToken);
        if (cameras.Count == 0)
        {
            return BadRequest("No cameras found for re-embed.");
        }

        var cameraById = cameras.ToDictionary(camera => camera.CameraId, StringComparer.OrdinalIgnoreCase);
        var passwordByCameraId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var camera in cameras)
        {
            passwordByCameraId[camera.CameraId] = string.IsNullOrWhiteSpace(camera.RtspPasswordEncrypted)
                ? string.Empty
                : _secretProtector.DecryptFromBase64(camera.RtspPasswordEncrypted);
        }

        var featureVersionByCamera = NormalizeFeatureVersionMap(request.FeatureVersionByCamera);
        var maxEvents = ClampLimit(request.MaxEvents, 1, 5000);
        var errors = new List<string>();
        var result = new ReembedResult();

        var fromUtc = request.FromUtc?.UtcDateTime;
        var toUtc = request.ToUtc?.UtcDateTime;
        var records = await _faceEventRepository.ListAsync(fromUtc, toUtc, maxEvents, cancellationToken);

        foreach (var record in records)
        {
            if (!cameraById.TryGetValue(record.CameraId, out var camera))
            {
                result.Skipped++;
                AddError(errors, $"event {record.Id}: camera {record.CameraId} not found");
                continue;
            }

            var password = passwordByCameraId[camera.CameraId];
            var targetFeatureVersion = ResolveTargetFeatureVersion(
                request.TargetFeatureVersion,
                featureVersionByCamera,
                camera.CameraId);
            targetFeatureVersion = FeatureVersionHelper.Combine(targetFeatureVersion, camera.CameraSeries);

            if (string.IsNullOrWhiteSpace(record.BestshotPath))
            {
                result.Skipped++;
                continue;
            }

            var bytes = await _bestshotResolver.LoadBestshotBytesAsync(record.BestshotPath, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                result.Skipped++;
                continue;
            }

            result.Processed++;
            if (request.DryRun)
            {
                result.Created++;
                continue;
            }

            try
            {
                var enrollment = await _enrollmentClient.EnrollAsync(
                    camera.IpAddress,
                    camera.RtspUsername,
                    password,
                    bytes,
                    cancellationToken);

                if (enrollment.FeatureVector.Length == 0)
                {
                    result.Failed++;
                    AddError(errors, $"event {record.Id}: empty feature");
                    continue;
                }

                await _faceEventIndex.UpsertAsync(new FaceEventIndexEntry
                {
                    EventId = record.Id,
                    EventTimeUtc = record.EventTimeUtc,
                    CameraId = record.CameraId,
                    FeatureVersion = targetFeatureVersion,
                    FeatureVector = enrollment.FeatureVector
                }, cancellationToken);

                result.Created++;
            }
            catch (EnrollmentTemplateException ex)
            {
                _logger.LogWarning(ex, "Re-embed template missing for event {EventId}", record.Id);
                result.Failed++;
                AddError(errors, $"event {record.Id}: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Re-embed failed for event {EventId}", record.Id);
                result.Failed++;
                AddError(errors, $"event {record.Id}: {ex.Message}");
            }
        }

        result.Errors = errors;
        return Ok(result);
    }

    private async Task<IReadOnlyList<Person>> LoadPersonsAsync(
        IReadOnlyCollection<Guid>? ids,
        CancellationToken cancellationToken)
    {
        if (ids is null || ids.Count == 0)
        {
            return await _personRepository.ListAsync(cancellationToken);
        }

        var unique = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        var results = new List<Person>();
        foreach (var id in unique)
        {
            var person = await _personRepository.GetAsync(id, cancellationToken);
            if (person is not null)
            {
                results.Add(person);
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<CameraCredential>> ResolveCamerasAsync(
        string? cameraId,
        IReadOnlyCollection<string>? cameraIds,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(cameraId))
        {
            var camera = await _cameraRepository.GetAsync(cameraId.Trim(), cancellationToken);
            return camera is null ? Array.Empty<CameraCredential>() : new List<CameraCredential> { camera };
        }

        if (cameraIds is not null && cameraIds.Count > 0)
        {
            var uniqueIds = cameraIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueIds.Count == 0)
            {
                return Array.Empty<CameraCredential>();
            }

            var results = new List<CameraCredential>(uniqueIds.Count);
            foreach (var id in uniqueIds)
            {
                var camera = await _cameraRepository.GetAsync(id, cancellationToken);
                if (camera is not null)
                {
                    results.Add(camera);
                }
            }

            return results;
        }

        return await _cameraRepository.ListAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, string>? NormalizeFeatureVersionMap(
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
            {
                continue;
            }

            normalized[kvp.Key.Trim()] = kvp.Value.Trim();
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private string ResolveTargetFeatureVersion(
        string? target,
        IReadOnlyDictionary<string, string>? featureVersionByCamera,
        string cameraId)
    {
        if (featureVersionByCamera is not null
            && featureVersionByCamera.TryGetValue(cameraId, out var mapped)
            && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped.Trim();
        }

        return ResolveTargetFeatureVersion(target);
    }

    private string ResolveTargetFeatureVersion(string? target)
    {
        var resolved = string.IsNullOrWhiteSpace(target)
            ? _enrollmentOptions.FeatureVersion
            : target.Trim();

        return string.IsNullOrWhiteSpace(resolved) ? "legacy" : resolved;
    }

    private bool IsSameFeatureVersion(string? existing, string target)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return false;
        }

        var normalized = existing.Trim();
        if (string.Equals(normalized, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(_enrollmentOptions.AppName)
            && string.Equals(normalized, _enrollmentOptions.AppName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_enrollmentOptions.FeatureVersion, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static int ClampLimit(int value, int min, int max)
    {
        var resolved = value <= 0 ? min : value;
        return Math.Clamp(resolved, min, max);
    }

    private static void AddError(List<string> errors, string message)
    {
        if (errors.Count >= MaxErrors)
        {
            return;
        }

        errors.Add(message);
    }
}
