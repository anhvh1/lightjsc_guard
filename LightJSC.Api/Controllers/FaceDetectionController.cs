using LightJSC.Api.Contracts;
using LightJSC.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LightJSC.Api.Controllers;

[ApiController]
[Route("api/v1/persons")]
public sealed class FaceDetectionController : ControllerBase
{
    private readonly IFaceDetectorService _detector;
    private readonly ILogger<FaceDetectionController> _logger;

    public FaceDetectionController(IFaceDetectorService detector, ILogger<FaceDetectionController> logger)
    {
        _detector = detector;
        _logger = logger;
    }

    [HttpPost("face-detect")]
    public ActionResult<IReadOnlyList<FaceDetectResponse>> DetectFaces(
        [FromBody] FaceDetectRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            return BadRequest("ImageBase64 is required.");
        }

        if (!TryDecodeBase64(request.ImageBase64, out var imageBytes))
        {
            return BadRequest("ImageBase64 is invalid.");
        }

        try
        {
            _logger.LogInformation("Face detect request size={Size} bytes, MaxFaces={MaxFaces}.",
                imageBytes.Length,
                request.MaxFaces);
            var detections = _detector.DetectFaces(imageBytes);
            if (request.MaxFaces.HasValue && request.MaxFaces.Value > 0)
            {
                detections = detections.Take(request.MaxFaces.Value).ToList();
            }

            var response = detections
                .Select((face, index) => new FaceDetectResponse
                {
                    FaceId = index.ToString(),
                    Score = face.Score,
                    Box = new FaceBoxResponse
                    {
                        X = face.Box.X,
                        Y = face.Box.Y,
                        Width = face.Box.Width,
                        Height = face.Box.Height
                    },
                    ThumbnailBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(face.FaceJpeg)}"
                })
                .ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face detection failed.");
            return StatusCode(500, $"Face detection failed: {ex.Message}");
        }
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
}
