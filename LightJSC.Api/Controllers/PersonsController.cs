using System.Security.Cryptography;
using LightJSC.Api.Contracts;
using LightJSC.Core.Helpers;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Enrollment;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

/// <summary>
/// Manage local persons and face enrollments stored in Postgres.
/// </summary>
[ApiController]
[Route("api/v1/persons")]
public sealed class PersonsController : ControllerBase
{
    private readonly IPersonRepository _personRepository;
    private readonly IFaceTemplateRepository _templateRepository;
    private readonly ICameraRepository _cameraRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly IFaceEnrollmentClient _enrollmentClient;
    private readonly IVectorIndex _vectorIndex;
    private readonly EnrollmentOptions _enrollmentOptions;
    private readonly ILogger<PersonsController> _logger;

    public PersonsController(
        IPersonRepository personRepository,
        IFaceTemplateRepository templateRepository,
        ICameraRepository cameraRepository,
        ISecretProtector secretProtector,
        IFaceEnrollmentClient enrollmentClient,
        IVectorIndex vectorIndex,
        IOptions<EnrollmentOptions> enrollmentOptions,
        ILogger<PersonsController> logger)
    {
        _personRepository = personRepository;
        _templateRepository = templateRepository;
        _cameraRepository = cameraRepository;
        _secretProtector = secretProtector;
        _enrollmentClient = enrollmentClient;
        _vectorIndex = vectorIndex;
        _enrollmentOptions = enrollmentOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Create a new person record.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PersonResponse>> Create([FromBody] PersonRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest("Code is required.");
        }

        if (!TryNormalizeListType(request.ListType, out var listType))
        {
            return BadRequest("ListType must be WhiteList, BlackList, or empty.");
        }

        var existing = await _personRepository.GetByCodeAsync(request.Code, cancellationToken);
        if (existing is not null)
        {
            return Conflict("Code already exists.");
        }

        var now = DateTime.UtcNow;
        var person = new Person
        {
            Id = Guid.NewGuid(),
            Code = request.Code.Trim(),
            FirstName = request.FirstName?.Trim() ?? string.Empty,
            LastName = request.LastName?.Trim() ?? string.Empty,
            PersonalId = NormalizeOptional(request.PersonalId),
            DocumentNumber = NormalizeOptional(request.DocumentNumber),
            DateOfBirth = request.DateOfBirth,
            DateOfIssue = request.DateOfIssue,
            Address = NormalizeOptional(request.Address),
            RawQrPayload = NormalizeOptional(request.RawQrPayload),
            Gender = request.Gender?.Trim(),
            Age = request.Age,
            Remarks = request.Remarks?.Trim(),
            Category = request.Category?.Trim(),
            ListType = listType,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _personRepository.AddAsync(person, cancellationToken);
        var summaries = await GetTemplateSummariesAsync(new[] { person.Id }, cancellationToken);
        return Created($"/api/v1/persons/{person.Id}", ToResponse(person, summaries));
    }

    /// <summary>
    /// Update a person record.
    /// </summary>
    [HttpPut("{personId:guid}")]
    public async Task<ActionResult<PersonResponse>> Update(Guid personId, [FromBody] PersonRequest request, CancellationToken cancellationToken)
    {
        var person = await _personRepository.GetAsync(personId, cancellationToken);
        if (person is null)
        {
            return NotFound();
        }

        if (request.ListType is not null)
        {
            if (!TryNormalizeListType(request.ListType, out var listType))
            {
                return BadRequest("ListType must be WhiteList, BlackList, or empty.");
            }

            person.ListType = listType;
        }

        if (!string.IsNullOrWhiteSpace(request.Code) && !string.Equals(request.Code, person.Code, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _personRepository.GetByCodeAsync(request.Code, cancellationToken);
            if (existing is not null && existing.Id != personId)
            {
                return Conflict("Code already exists.");
            }

            person.Code = request.Code.Trim();
        }

        person.FirstName = request.FirstName?.Trim() ?? person.FirstName;
        person.LastName = request.LastName?.Trim() ?? person.LastName;
        person.PersonalId = NormalizeOptional(request.PersonalId);
        person.DocumentNumber = NormalizeOptional(request.DocumentNumber);
        person.DateOfBirth = request.DateOfBirth;
        person.DateOfIssue = request.DateOfIssue;
        person.Address = NormalizeOptional(request.Address);
        person.RawQrPayload = NormalizeOptional(request.RawQrPayload);
        person.Gender = NormalizeOptional(request.Gender);
        person.Remarks = NormalizeOptional(request.Remarks);
        person.Category = NormalizeOptional(request.Category);
        person.Age = request.Age;

        person.IsActive = request.IsActive;
        person.UpdatedAt = DateTime.UtcNow;

        await _personRepository.UpdateAsync(person, cancellationToken);
        var summaries = await GetTemplateSummariesAsync(new[] { person.Id }, cancellationToken);
        return Ok(ToResponse(person, summaries));
    }

    /// <summary>
    /// List all persons.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PersonResponse>>> List(CancellationToken cancellationToken)
    {
        var people = await _personRepository.ListAsync(cancellationToken);
        var summaries = await GetTemplateSummariesAsync(people.Select(person => person.Id).ToArray(), cancellationToken);
        return Ok(people.Select(person => ToResponse(person, summaries)).ToList());
    }

    /// <summary>
    /// Get a person by id.
    /// </summary>
    [HttpGet("{personId:guid}")]
    public async Task<ActionResult<PersonResponse>> Get(Guid personId, CancellationToken cancellationToken)
    {
        var person = await _personRepository.GetAsync(personId, cancellationToken);
        if (person is null)
        {
            return NotFound();
        }

        var summaries = await GetTemplateSummariesAsync(new[] { personId }, cancellationToken);
        return Ok(ToResponse(person, summaries));
    }

    /// <summary>
    /// Delete a person and remove watchlist entries. Use hard=false for soft delete.
    /// </summary>
    [HttpDelete("{personId:guid}")]
    public async Task<IActionResult> Delete(
        Guid personId,
        CancellationToken cancellationToken,
        [FromQuery] bool hard = true)
    {
        var templates = await _templateRepository.ListByPersonAsync(personId, cancellationToken);
        if (hard)
        {
            await _personRepository.DeleteHardAsync(personId, cancellationToken);
        }
        else
        {
            await _personRepository.DeleteAsync(personId, cancellationToken);
        }

        foreach (var template in templates)
        {
            _vectorIndex.Remove(template.Id.ToString());
        }

        return NoContent();
    }

    /// <summary>
    /// Purge all persons and templates. Set confirm=true to execute.
    /// </summary>
    [HttpPost("purge")]
    public async Task<IActionResult> Purge(
        [FromQuery] bool confirm,
        [FromQuery] bool includeEvents,
        CancellationToken cancellationToken)
    {
        if (!confirm)
        {
            return BadRequest("confirm=true is required.");
        }

        await _personRepository.PurgeAsync(includeEvents, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Enroll a face by sending the image to the camera CGI.
    /// </summary>
    [HttpPost("{personId:guid}/enroll")]
    public async Task<ActionResult<FaceTemplateResponse>> Enroll(Guid personId, [FromBody] EnrollFaceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CameraId))
        {
            return BadRequest("CameraId is required.");
        }

        if (!TryDecodeBase64(request.ImageBase64, out var imageBytes))
        {
            return BadRequest("ImageBase64 is invalid.");
        }

        var person = await _personRepository.GetAsync(personId, cancellationToken);
        if (person is null)
        {
            return NotFound("Person not found.");
        }

        var cameras = await _cameraRepository.ListAsync(cancellationToken);
        var enabledCameras = cameras.Where(cam => cam.Enabled).ToList();
        if (enabledCameras.Count == 0)
        {
            return BadRequest("No cameras available for enrollment.");
        }

        var preferredAny = cameras.FirstOrDefault(cam => cam.CameraId == request.CameraId);
        if (preferredAny is null)
        {
            return NotFound("Camera not found.");
        }

        var preferred = enabledCameras.FirstOrDefault(cam => cam.CameraId == request.CameraId);
        if (preferred is null)
        {
            var preferredSeries = FeatureVersionHelper.NormalizeSeries(preferredAny.CameraSeries);
            preferred = enabledCameras.FirstOrDefault(cam =>
                string.Equals(
                    FeatureVersionHelper.NormalizeSeries(cam.CameraSeries),
                    preferredSeries,
                    StringComparison.OrdinalIgnoreCase))
                ?? enabledCameras.OrderBy(cam => cam.CameraId, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        if (preferred is null)
        {
            return BadRequest("No cameras available for enrollment.");
        }

        var selectedCameras = SelectSeriesCameras(enabledCameras, preferred);
        if (selectedCameras.Count == 0)
        {
            return BadRequest("No cameras available for enrollment.");
        }

        var createdTemplates = new List<FaceTemplate>();
        FaceTemplate? preferredTemplate = null;
        var hadFailure = false;
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
                _logger.LogWarning(ex, "Enrollment template missing for camera {CameraId}", camera.CameraId);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Enrollment CGI failed for camera {CameraId}", camera.CameraId);
                hadFailure = true;
                continue;
            }

            if (enrollment.FeatureBytes.Length == 0)
            {
                hadFailure = true;
                continue;
            }

            var featureHash = Convert.ToHexString(SHA256.HashData(enrollment.FeatureBytes));
            if (await _templateRepository.ExistsByHashAsync(personId, featureHash, cancellationToken))
            {
                continue;
            }

            var now = DateTime.UtcNow;
            var template = new FaceTemplate
            {
                Id = Guid.NewGuid(),
                PersonId = person.Id,
                FeatureBytes = enrollment.FeatureBytes,
                L2Norm = enrollment.L2Norm,
                FeatureVersion = FeatureVersionHelper.Combine(_enrollmentOptions.FeatureVersion, camera.CameraSeries),
                FaceImageJpeg = request.StoreFaceImage ? imageBytes : null,
                SourceCameraId = string.IsNullOrWhiteSpace(request.SourceCameraId) ? camera.CameraId : request.SourceCameraId,
                FeatureHash = featureHash,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _templateRepository.AddAsync(template, cancellationToken);
            createdTemplates.Add(template);
            if (string.Equals(camera.CameraId, preferred.CameraId, StringComparison.OrdinalIgnoreCase))
            {
                preferredTemplate ??= template;
            }
        }

        if (createdTemplates.Count == 0)
        {
            if (hadFailure)
            {
                return StatusCode(502, "Enrollment CGI failed.");
            }

            return Conflict("Face feature already enrolled.");
        }

        var responseTemplate = preferredTemplate ?? createdTemplates[0];

        return Ok(ToTemplateResponse(responseTemplate));
    }

    /// <summary>
    /// List enrolled templates for a person.
    /// </summary>
    [HttpGet("{personId:guid}/templates")]
    public async Task<ActionResult<IReadOnlyList<FaceTemplateResponse>>> ListTemplates(Guid personId, CancellationToken cancellationToken)
    {
        var person = await _personRepository.GetAsync(personId, cancellationToken);
        if (person is null)
        {
            return NotFound("Person not found.");
        }

        var templates = await _templateRepository.ListByPersonAsync(personId, cancellationToken);
        return Ok(templates.Select(ToTemplateResponse).ToList());
    }

    /// <summary>
    /// Deactivate a face template.
    /// </summary>
    [HttpDelete("{personId:guid}/templates/{templateId:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid personId, Guid templateId, CancellationToken cancellationToken)
    {
        var person = await _personRepository.GetAsync(personId, cancellationToken);
        if (person is null)
        {
            return NotFound("Person not found.");
        }

        var template = await _templateRepository.GetAsync(templateId, cancellationToken);
        if (template is null || template.PersonId != personId)
        {
            return NotFound("Template not found.");
        }

        await _templateRepository.DeleteAsync(templateId, cancellationToken);
        _vectorIndex.Remove(templateId.ToString());
        return NoContent();
    }

    /// <summary>
    /// Update a face template active status.
    /// </summary>
    [HttpPut("{personId:guid}/templates/{templateId:guid}/status")]
    public async Task<ActionResult<FaceTemplateResponse>> UpdateTemplateStatus(
        Guid personId,
        Guid templateId,
        [FromBody] TemplateStatusRequest request,
        CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetAsync(templateId, cancellationToken);
        if (template is null || template.PersonId != personId)
        {
            return NotFound("Template not found.");
        }

        if (template.IsActive == request.IsActive)
        {
            return Ok(ToTemplateResponse(template));
        }

        await _templateRepository.SetActiveAsync(templateId, request.IsActive, cancellationToken);
        if (!request.IsActive)
        {
            _vectorIndex.Remove(templateId.ToString());
        }

        var updated = await _templateRepository.GetAsync(templateId, cancellationToken);
        if (updated is null)
        {
            return NotFound("Template not found.");
        }

        return Ok(ToTemplateResponse(updated));
    }

    private static PersonResponse ToResponse(Person person, IReadOnlyDictionary<Guid, TemplateSummary> summaries)
    {
        summaries.TryGetValue(person.Id, out var summary);
        return new PersonResponse
        {
            Id = person.Id,
            Code = person.Code,
            FirstName = person.FirstName,
            LastName = person.LastName,
            PersonalId = person.PersonalId,
            DocumentNumber = person.DocumentNumber,
            DateOfBirth = person.DateOfBirth,
            DateOfIssue = person.DateOfIssue,
            Address = person.Address,
            RawQrPayload = person.RawQrPayload,
            Gender = person.Gender,
            Age = person.Age,
            Remarks = person.Remarks,
            Category = person.Category,
            ListType = person.ListType,
            IsActive = person.IsActive,
            IsEnrolled = summary.IsEnrolled,
            EnrolledFaceImageBase64 = summary.ImageBase64,
            CreatedAt = person.CreatedAt,
            UpdatedAt = person.UpdatedAt
        };
    }

    private static FaceTemplateResponse ToTemplateResponse(FaceTemplate template)
    {
        return new FaceTemplateResponse
        {
            Id = template.Id,
            PersonId = template.PersonId,
            FeatureVersion = template.FeatureVersion,
            L2Norm = template.L2Norm,
            SourceCameraId = template.SourceCameraId,
            IsActive = template.IsActive,
            FaceImageBase64 = ToImageDataUrl(template.FaceImageJpeg),
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }

    private static string? ToImageDataUrl(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
    }

    private async Task<IReadOnlyDictionary<Guid, TemplateSummary>> GetTemplateSummariesAsync(
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return new Dictionary<Guid, TemplateSummary>();
        }

        var ids = personIds.Distinct().ToArray();
        var summaries = ids.ToDictionary(id => id, _ => new TemplateSummary(false, null));
        var templates = await _templateRepository.ListActiveByPersonIdsAsync(ids, cancellationToken);
        foreach (var template in templates)
        {
            if (!summaries.TryGetValue(template.PersonId, out var summary))
            {
                continue;
            }

            var image = summary.ImageBase64;
            if (string.IsNullOrWhiteSpace(image) && template.FaceImageJpeg is { Length: > 0 })
            {
                image = ToImageDataUrl(template.FaceImageJpeg);
            }

            summaries[template.PersonId] = summary with { IsEnrolled = true, ImageBase64 = image };
        }

        return summaries;
    }

    private readonly record struct TemplateSummary(bool IsEnrolled, string? ImageBase64);

    private static bool TryNormalizeListType(string? value, out string? normalized)
    {
        normalized = null;
        if (value is null)
        {
            return true;
        }

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
        {
            normalized = null;
            return true;
        }

        if (string.Equals(trimmed, PersonListTypes.WhiteList, StringComparison.OrdinalIgnoreCase))
        {
            normalized = PersonListTypes.WhiteList;
            return true;
        }

        if (string.Equals(trimmed, PersonListTypes.BlackList, StringComparison.OrdinalIgnoreCase))
        {
            normalized = PersonListTypes.BlackList;
            return true;
        }

        return false;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
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

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static IReadOnlyList<CameraCredential> SelectSeriesCameras(
        IReadOnlyCollection<CameraCredential> cameras,
        CameraCredential preferred)
    {
        var groups = cameras
            .GroupBy(camera => FeatureVersionHelper.NormalizeSeries(camera.CameraSeries), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        var selected = new List<CameraCredential>();
        foreach (var group in groups)
        {
            var chosen = group.FirstOrDefault(camera => camera.CameraId == preferred.CameraId)
                ?? group.OrderBy(camera => camera.CameraId, StringComparer.OrdinalIgnoreCase).First();
            selected.Add(chosen);
        }

        return selected;
    }
}

