using System.Text.Json;
using LightJSC.Api.Contracts;
using LightJSC.Api.Services;
using LightJSC.Core.Models;
using LightJSC.Infrastructure.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/face-events")]
public sealed class FaceEventsController : ControllerBase
{
    private readonly FaceEventSearchRepository _searchRepository;
    private readonly BestshotResolver _bestshotResolver;
    private readonly ILogger<FaceEventsController> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public FaceEventsController(
        FaceEventSearchRepository searchRepository,
        BestshotResolver bestshotResolver,
        ILogger<FaceEventsController> logger)
    {
        _searchRepository = searchRepository;
        _bestshotResolver = bestshotResolver;
        _logger = logger;
    }

    [HttpPost("search")]
    public async Task<ActionResult<FaceEventSearchResponse>> Search(
        [FromBody] FaceEventSearchRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new FaceEventSearchRequest();
        var filter = BuildFilter(request);
        var result = await _searchRepository.SearchAsync(filter, cancellationToken);
        var response = await BuildSearchResponseAsync(result.Items, result.Total, request.IncludeBestshot, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FaceEventResponse>> GetById(
        Guid id,
        [FromQuery] bool includeBestshot,
        CancellationToken cancellationToken)
    {
        var row = await _searchRepository.GetByIdAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        var response = await BuildResponseAsync(row, includeBestshot, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}/bestshot")]
    public async Task<ActionResult<BestshotResponse>> GetBestshot(Guid id, CancellationToken cancellationToken)
    {
        var row = await _searchRepository.GetByIdAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound();
        }

        var base64 = await _bestshotResolver.LoadBestshotBase64Async(row.BestshotPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(base64))
        {
            return NotFound();
        }

        return Ok(new BestshotResponse { Base64 = base64 });
    }

    private FaceEventSearchFilter BuildFilter(FaceEventSearchRequest request)
    {
        return new FaceEventSearchFilter
        {
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            CameraIds = request.CameraIds,
            IsKnown = request.IsKnown,
            ListType = request.ListType,
            Gender = request.Gender,
            AgeMin = request.AgeMin,
            AgeMax = request.AgeMax,
            Mask = request.Mask,
            ScoreMin = request.ScoreMin,
            SimilarityMin = request.SimilarityMin,
            PersonQuery = request.PersonQuery,
            PersonIds = request.PersonIds,
            Category = request.Category,
            HasFeature = request.HasFeature,
            WatchlistEntryIds = request.WatchlistEntryIds,
            Page = request.Page,
            PageSize = request.PageSize
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

    private async Task<FaceEventResponse> BuildResponseAsync(
        FaceEventSearchRow row,
        bool includeBestshot,
        CancellationToken cancellationToken)
    {
        var bestshotBase64 = includeBestshot
            ? await _bestshotResolver.LoadBestshotBase64Async(row.BestshotPath, cancellationToken)
            : null;
        return MapRow(row, bestshotBase64);
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
}
