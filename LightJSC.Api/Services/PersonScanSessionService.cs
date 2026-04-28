using System.Collections.Concurrent;
using LightJSC.Api.Contracts;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Services;

public sealed class PersonScanSessionService
{
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecretProtector _secretProtector;
    private readonly IRtspSnapshotService _snapshotService;
    private readonly IQrCodeDecoder _qrCodeDecoder;
    private readonly IFaceDetectorService _faceDetectorService;
    private readonly PersonScanOptions _options;
    private readonly ILogger<PersonScanSessionService> _logger;

    public PersonScanSessionService(
        IServiceScopeFactory scopeFactory,
        ISecretProtector secretProtector,
        IRtspSnapshotService snapshotService,
        IQrCodeDecoder qrCodeDecoder,
        IFaceDetectorService faceDetectorService,
        IOptions<PersonScanOptions> options,
        ILogger<PersonScanSessionService> logger)
    {
        _scopeFactory = scopeFactory;
        _secretProtector = secretProtector;
        _snapshotService = snapshotService;
        _qrCodeDecoder = qrCodeDecoder;
        _faceDetectorService = faceDetectorService;
        _options = options.Value;
        _logger = logger;
    }

    public Task<PersonScanResultResponse> CreateSessionAsync(
        CreatePersonScanSessionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PruneExpiredSessions();

        var now = DateTime.UtcNow;
        var state = new SessionState
        {
            SessionId = Guid.NewGuid(),
            CameraId = NormalizeOptional(request.CameraId),
            Status = "Previewing",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _sessions[state.SessionId] = state;
        return Task.FromResult(ToResponse(state));
    }

    public async Task<PersonScanResultResponse?> ScanAsync(
        Guid sessionId,
        PersonScanRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PruneExpiredSessions();

        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            return null;
        }

        state.UpdatedAtUtc = DateTime.UtcNow;
        state.ErrorMessage = null;
        var mode = NormalizeMode(request.Mode);

        if (request.ResetQr)
        {
            state.RawQrPayload = null;
            state.Person = null;
        }

        if (request.ResetFace)
        {
            state.FaceImageBase64 = null;
        }

        if (string.IsNullOrWhiteSpace(state.CameraId))
        {
            state.Status = "Error";
            state.ErrorMessage = "CameraId is required.";
            return ToResponse(state);
        }

        using var scope = _scopeFactory.CreateScope();
        var cameraRepository = scope.ServiceProvider.GetRequiredService<ICameraRepository>();
        var cameras = await cameraRepository.ListAsync(cancellationToken);
        var camera = cameras.FirstOrDefault(item =>
            string.Equals(item.CameraId, state.CameraId, StringComparison.OrdinalIgnoreCase));
        if (camera is null)
        {
            state.Status = "Error";
            state.ErrorMessage = "Camera not found.";
            return ToResponse(state);
        }

        try
        {
            var snapshotBytes = await _snapshotService.CaptureJpegAsync(
                BuildRtspUri(camera),
                cancellationToken);

            state.SnapshotImageBase64 = ToImageDataUrl(snapshotBytes);
            state.ScannedAtUtc = DateTime.UtcNow;

            if (mode is "qr" or "single")
            {
                var qrPayload = _qrCodeDecoder.Decode(snapshotBytes);
                if (!string.IsNullOrWhiteSpace(qrPayload))
                {
                    state.RawQrPayload = qrPayload;
                    state.Person = ParseQrPayload(qrPayload);
                }
            }

            if (mode is "face" or "single")
            {
                var face = _faceDetectorService
                    .DetectFaces(snapshotBytes)
                    .OrderByDescending(item => item.Score)
                    .FirstOrDefault();

                if (face is not null)
                {
                    state.FaceImageBase64 = ToImageDataUrl(face.FaceJpeg);
                }
            }

            state.Status = DetermineStatus(state, mode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System camera person scan failed. SessionId={SessionId}", sessionId);
            state.Status = "Error";
            state.ErrorMessage = ex.Message;
        }

        state.UpdatedAtUtc = DateTime.UtcNow;
        return ToResponse(state);
    }

    private static string DetermineStatus(SessionState state, string mode)
    {
        if (state.Person is not null && !string.IsNullOrWhiteSpace(state.FaceImageBase64))
        {
            return "Ready";
        }

        if (state.Person is not null)
        {
            return "Step2Face";
        }

        if (!string.IsNullOrWhiteSpace(state.FaceImageBase64))
        {
            return "Partial";
        }

        return mode switch
        {
            "qr" => "Step1Qr",
            "face" => "Step2Face",
            _ => "Previewing"
        };
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = mode?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "preview" => "preview",
            "qr" => "qr",
            "face" => "face",
            _ => "single"
        };
    }

    private void PruneExpiredSessions()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-Math.Max(5, _options.SessionRetentionMinutes));
        foreach (var item in _sessions)
        {
            if (item.Value.UpdatedAtUtc < threshold)
            {
                _sessions.TryRemove(item.Key, out _);
            }
        }
    }

    private Uri BuildRtspUri(CameraCredential camera)
    {
        var password = string.IsNullOrWhiteSpace(camera.RtspPasswordEncrypted)
            ? string.Empty
            : _secretProtector.DecryptFromBase64(camera.RtspPasswordEncrypted);

        var (host, port) = ParseHostPort(camera.IpAddress);
        var builder = new UriBuilder
        {
            Scheme = "rtsp",
            Host = host,
            Port = port,
            UserName = camera.RtspUsername,
            Password = password,
            Path = camera.RtspPath.StartsWith("/", StringComparison.Ordinal)
                ? camera.RtspPath
                : "/" + camera.RtspPath,
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

    private static PersonScanResultResponse ToResponse(SessionState state)
    {
        return new PersonScanResultResponse
        {
            SessionId = state.SessionId,
            CameraId = state.CameraId,
            Status = state.Status,
            QrDetected = !string.IsNullOrWhiteSpace(state.RawQrPayload),
            FaceDetected = !string.IsNullOrWhiteSpace(state.FaceImageBase64),
            SnapshotImageBase64 = state.SnapshotImageBase64,
            FaceImageBase64 = state.FaceImageBase64,
            RawQrPayload = state.RawQrPayload,
            Person = state.Person,
            ErrorMessage = state.ErrorMessage,
            ScannedAtUtc = state.ScannedAtUtc,
            CreatedAtUtc = state.CreatedAtUtc,
            UpdatedAtUtc = state.UpdatedAtUtc
        };
    }

    private static PersonScanPersonResponse ParseQrPayload(string payload)
    {
        var fields = payload.Split('|');
        var personalId = GetField(fields, 0);
        var documentNumber = GetField(fields, 1);
        var fullName = GetField(fields, 2);
        var dateOfBirth = ParseCompactDate(GetField(fields, 3));
        var gender = GetField(fields, 4);
        var address = GetField(fields, 5);
        var dateOfIssue = ParseCompactDate(GetField(fields, 6));
        var (firstName, lastName) = SplitFullName(fullName);

        return new PersonScanPersonResponse
        {
            Code = documentNumber ?? personalId,
            PersonalId = personalId,
            DocumentNumber = documentNumber,
            FullName = fullName,
            FirstName = firstName,
            LastName = lastName,
            Gender = gender,
            DateOfBirth = dateOfBirth,
            DateOfIssue = dateOfIssue,
            Age = CalculateAge(dateOfBirth),
            Address = address,
            RawQrPayload = payload
        };
    }

    private static string? GetField(IReadOnlyList<string> fields, int index)
    {
        if (index < 0 || index >= fields.Count)
        {
            return null;
        }

        return NormalizeOptional(fields[index]);
    }

    private static (string FirstName, string LastName) SplitFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (string.Empty, string.Empty);
        }

        var tokens = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1)
        {
            return (tokens[0], string.Empty);
        }

        return (string.Join(' ', tokens[..^1]), tokens[^1]);
    }

    private static DateOnly? ParseCompactDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 8)
        {
            return null;
        }

        return int.TryParse(value[..2], out var day)
            && int.TryParse(value.Substring(2, 2), out var month)
            && int.TryParse(value.Substring(4, 4), out var year)
            && DateOnly.TryParse($"{year:D4}-{month:D2}-{day:D2}", out var parsed)
            ? parsed
            : null;
    }

    private static int? CalculateAge(DateOnly? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var age = today.Year - value.Value.Year;
        if (value.Value > today.AddYears(-age))
        {
            age--;
        }

        return age < 0 ? null : age;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string ToImageDataUrl(byte[] imageBytes)
    {
        return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
    }

    private sealed class SessionState
    {
        public Guid SessionId { get; init; }
        public string? CameraId { get; set; }
        public string Status { get; set; } = "Previewing";
        public string? SnapshotImageBase64 { get; set; }
        public string? FaceImageBase64 { get; set; }
        public string? RawQrPayload { get; set; }
        public PersonScanPersonResponse? Person { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? ScannedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
