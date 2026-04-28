using LightJSC.Api.Contracts;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Sockets;

namespace LightJSC.Api.Controllers;

/// <summary>
/// Manage RTSP camera credentials and profiles stored in Postgres.
/// </summary>
[ApiController]
[Route("api/v1/cameras")]
public sealed class CamerasController : ControllerBase
{
    private readonly ICameraRepository _cameraRepository;
    private readonly ISecretProtector _secretProtector;
    private readonly IRtspMetadataClient _rtspMetadataClient;
    private readonly ILogger<CamerasController> _logger;

    public CamerasController(
        ICameraRepository cameraRepository,
        ISecretProtector secretProtector,
        IRtspMetadataClient rtspMetadataClient,
        ILogger<CamerasController> logger)
    {
        _cameraRepository = cameraRepository;
        _secretProtector = secretProtector;
        _rtspMetadataClient = rtspMetadataClient;
        _logger = logger;
    }

    /// <summary>
    /// Create a camera credential record.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CameraResponse>> Create([FromBody] CameraRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CameraId) || string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return BadRequest("CameraId and IpAddress are required.");
        }

        var existing = await _cameraRepository.GetAsync(request.CameraId, cancellationToken);
        if (existing is not null)
        {
            return Conflict("CameraId already exists.");
        }

        var encrypted = string.IsNullOrWhiteSpace(request.RtspPassword)
            ? string.Empty
            : _secretProtector.EncryptToBase64(request.RtspPassword);

        var now = DateTime.UtcNow;
        var camera = new CameraCredential
        {
            CameraId = request.CameraId,
            Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim(),
            IpAddress = request.IpAddress,
            RtspUsername = request.RtspUsername,
            RtspPasswordEncrypted = encrypted,
            RtspProfile = string.IsNullOrWhiteSpace(request.RtspProfile) ? "def_profile1" : request.RtspProfile,
            RtspPath = string.IsNullOrWhiteSpace(request.RtspPath) ? "/ONVIF/MediaInput" : request.RtspPath,
            CameraSeries = string.IsNullOrWhiteSpace(request.CameraSeries) ? null : request.CameraSeries.Trim(),
            CameraModel = string.IsNullOrWhiteSpace(request.CameraModel) ? null : request.CameraModel.Trim(),
            Enabled = request.Enabled,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _cameraRepository.AddAsync(camera, cancellationToken);
        return Created($"/api/v1/cameras/{camera.CameraId}", ToResponse(camera));
    }

    /// <summary>
    /// Update an existing camera record.
    /// </summary>
    [HttpPut("{cameraId}")]
    public async Task<ActionResult<CameraResponse>> Update(string cameraId, [FromBody] CameraRequest request, CancellationToken cancellationToken)
    {
        var camera = await _cameraRepository.GetAsync(cameraId, cancellationToken);
        if (camera is null)
        {
            return NotFound();
        }

        camera.IpAddress = string.IsNullOrWhiteSpace(request.IpAddress) ? camera.IpAddress : request.IpAddress;
        camera.RtspUsername = string.IsNullOrWhiteSpace(request.RtspUsername) ? camera.RtspUsername : request.RtspUsername;
        if (!string.IsNullOrWhiteSpace(request.RtspPassword))
        {
            camera.RtspPasswordEncrypted = _secretProtector.EncryptToBase64(request.RtspPassword);
        }

        camera.RtspProfile = string.IsNullOrWhiteSpace(request.RtspProfile) ? camera.RtspProfile : request.RtspProfile;
        camera.RtspPath = string.IsNullOrWhiteSpace(request.RtspPath) ? camera.RtspPath : request.RtspPath;
        if (request.Code is not null)
        {
            camera.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        }
        if (request.CameraSeries is not null)
        {
            camera.CameraSeries = string.IsNullOrWhiteSpace(request.CameraSeries)
                ? null
                : request.CameraSeries.Trim();
        }

        if (request.CameraModel is not null)
        {
            camera.CameraModel = string.IsNullOrWhiteSpace(request.CameraModel)
                ? null
                : request.CameraModel.Trim();
        }
        camera.Enabled = request.Enabled;
        camera.UpdatedAt = DateTime.UtcNow;

        await _cameraRepository.UpdateAsync(camera, cancellationToken);
        return Ok(ToResponse(camera));
    }

    /// <summary>
    /// List all cameras.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CameraResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var cameras = await _cameraRepository.ListAsync(cancellationToken);
        return Ok(cameras.Select(ToResponse).ToList());
    }

    /// <summary>
    /// Delete a camera record.
    /// </summary>
    [HttpDelete("{cameraId}")]
    public async Task<IActionResult> Delete(string cameraId, CancellationToken cancellationToken)
    {
        await _cameraRepository.DeleteAsync(cameraId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Test RTSP metadata connectivity for a camera.
    /// </summary>
    [HttpPost("{cameraId}/test-rtsp")]
    public async Task<ActionResult<TestRtspResponse>> TestRtsp(string cameraId, CancellationToken cancellationToken)
    {
        var camera = await _cameraRepository.GetAsync(cameraId, cancellationToken);
        if (camera is null)
        {
            return NotFound();
        }

        var password = string.IsNullOrWhiteSpace(camera.RtspPasswordEncrypted)
            ? string.Empty
            : _secretProtector.DecryptFromBase64(camera.RtspPasswordEncrypted);

        var uri = BuildRtspUri(camera, password);
        var success = await _rtspMetadataClient.TestAsync(uri, TimeSpan.FromSeconds(10), cancellationToken);

        return Ok(new TestRtspResponse { Success = success });
    }

    /// <summary>
    /// Discover ONVIF cameras on the local network.
    /// </summary>
    [HttpGet("discover")]
    public async Task<ActionResult<IReadOnlyList<DiscoveredCameraResponse>>> Discover(
        [FromQuery] int? timeoutSeconds,
        [FromQuery] string? ipStart,
        [FromQuery] string? ipEnd,
        [FromServices] ICameraDiscoveryService discoveryService,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds ?? 4);
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromSeconds(15))
        {
            return BadRequest("timeoutSeconds must be between 1 and 15.");
        }

        uint? start = null;
        uint? end = null;
        IPAddress? startIp = null;
        IPAddress? endIp = null;
        if (!string.IsNullOrWhiteSpace(ipStart) || !string.IsNullOrWhiteSpace(ipEnd))
        {
            if (string.IsNullOrWhiteSpace(ipStart) || string.IsNullOrWhiteSpace(ipEnd))
            {
                return BadRequest("ipStart and ipEnd must be provided together.");
            }

            if (!TryParseIpv4(ipStart, out var parsedStart) || !TryParseIpv4(ipEnd, out var parsedEnd))
            {
                return BadRequest("ipStart and ipEnd must be valid IPv4 addresses.");
            }

            if (parsedStart > parsedEnd)
            {
                return BadRequest("ipStart must be less than or equal to ipEnd.");
            }

            start = parsedStart;
            end = parsedEnd;
            startIp = IPAddress.Parse(ipStart);
            endIp = IPAddress.Parse(ipEnd);
        }

        var cameras = await discoveryService.DiscoverAsync(timeout, startIp, endIp, cancellationToken);
        if (start.HasValue && end.HasValue)
        {
            cameras = cameras
                .Where(camera =>
                    TryParseIpv4(camera.IpAddress, out var ip) &&
                    ip >= start.Value &&
                    ip <= end.Value)
                .ToList();
        }
        var response = cameras.Select(camera => new DiscoveredCameraResponse
        {
            DeviceId = camera.DeviceId,
            IpAddress = camera.IpAddress,
            Name = camera.Name,
            Model = camera.Model,
            CameraSeries = camera.CameraSeries,
            MacAddress = camera.MacAddress,
            XAddr = camera.XAddr,
            Scopes = camera.Scopes
        }).ToList();

        return Ok(response);
    }

    private static CameraResponse ToResponse(CameraCredential camera)
    {
        return new CameraResponse
        {
            CameraId = camera.CameraId,
            Code = string.IsNullOrWhiteSpace(camera.Code) ? null : camera.Code,
            IpAddress = camera.IpAddress,
            RtspUsername = camera.RtspUsername,
            RtspProfile = camera.RtspProfile,
            RtspPath = camera.RtspPath,
            CameraSeries = string.IsNullOrWhiteSpace(camera.CameraSeries) ? null : camera.CameraSeries,
            CameraModel = string.IsNullOrWhiteSpace(camera.CameraModel) ? null : camera.CameraModel,
            Enabled = camera.Enabled,
            HasPassword = !string.IsNullOrWhiteSpace(camera.RtspPasswordEncrypted),
            CreatedAt = camera.CreatedAt,
            UpdatedAt = camera.UpdatedAt
        };
    }

    private static Uri BuildRtspUri(CameraCredential camera, string password)
    {
        var (host, port) = ParseHostPort(camera.IpAddress);
        var builder = new UriBuilder
        {
            Scheme = "rtsp",
            Host = host,
            Port = port,
            UserName = camera.RtspUsername,
            Password = password,
            Path = camera.RtspPath.StartsWith("/", StringComparison.Ordinal) ? camera.RtspPath : "/" + camera.RtspPath,
            Query = "profile=" + Uri.EscapeDataString(camera.RtspProfile)
        };

        return builder.Uri;
    }

    private static (string Host, int Port) ParseHostPort(string ipAddress)
    {
        var host = ipAddress;
        var port = 554;
        var parts = ipAddress.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && int.TryParse(parts[1], out var parsedPort))
        {
            host = parts[0];
            port = parsedPort;
        }

        return (host, port);
    }

    private static bool TryParseIpv4(string? value, out uint result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value.Trim();
        var host = raw.Split(':', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        result = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        return true;
    }
}

