using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Vector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightJSC.Infrastructure.Enrollment;

public sealed class AdamFaceEnrollmentClient : IFaceEnrollmentClient
{
    private static readonly byte[] CrLf = { (byte)'\r', (byte)'\n' };
    private static readonly byte[] CrLfCrLf = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
    private static readonly byte[] JpegMarker = { 0xFF, 0xD8, 0xFF };
    private static readonly Regex Base64Token = new("[A-Za-z0-9+/=]{200,}", RegexOptions.Compiled);
    private static readonly object ErrorLogLock = new();
    private static readonly object TraceLogLock = new();
    private readonly EnrollmentOptions _options;
    private readonly ILogger<AdamFaceEnrollmentClient> _logger;

    public AdamFaceEnrollmentClient(IOptions<EnrollmentOptions> options, ILogger<AdamFaceEnrollmentClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FaceEnrollmentResult> EnrollAsync(
        string cameraIpAddress,
        string username,
        string password,
        byte[] imageJpeg,
        CancellationToken cancellationToken)
    {
        if (imageJpeg.Length == 0)
        {
            throw new InvalidOperationException("Enrollment image is empty.");
        }

        var uri = BuildCgiUri(cameraIpAddress);
        var appData = BuildAppData(imageJpeg);
        var base64Image = appData.Base64;

        var credentialCache = new CredentialCache();
        var baseUri = new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}/");
        credentialCache.Add(baseUri, "Digest", new NetworkCredential(username, password));

        using var handler = new HttpClientHandler
        {
            PreAuthenticate = false,
            Credentials = credentialCache,
            UseDefaultCredentials = false
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(3, _options.TimeoutSeconds))
        };

        var formFields = new[]
        {
            new KeyValuePair<string, string>("methodName", "sendDataToAdamApplication"),
            new KeyValuePair<string, string>("appName", _options.AppName),
            new KeyValuePair<string, string>("s_appDataType", _options.AppDataType.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("s_appData", base64Image)
        };

        using var formContent = new FormUrlEncodedContent(formFields);
        var formPayload = await formContent.ReadAsStringAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = formContent
        };
        request.Headers.ExpectContinue = true;
        var requestContentType = request.Content.Headers.ContentType?.ToString() ?? "application/x-www-form-urlencoded";

        HttpResponseMessage? response = null;
        var errorLogged = false;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var responseText = TryDecodeUtf8(responseBytes);
                var responseBase64 = Convert.ToBase64String(responseBytes);

                WriteEnrollmentError(
                    errorKind: "http_status",
                    uri: uri,
                    cameraIpAddress: cameraIpAddress,
                    username: username,
                    appData: appData,
                    requestContentType: requestContentType,
                    requestPayload: formPayload,
                    requestExpectContinue: request.Headers.ExpectContinue,
                    statusCode: response.StatusCode,
                    responseText: responseText,
                    responseBase64: responseBase64,
                    exceptionMessage: null,
                    parseError: null);

                WriteEnrollmentTrace(
                    uri: uri,
                    cameraIpAddress: cameraIpAddress,
                    username: username,
                    appData: appData,
                    requestContentType: requestContentType,
                    requestPayload: formPayload,
                    requestExpectContinue: request.Headers.ExpectContinue,
                    statusCode: response.StatusCode,
                    responseContentType: response.Content.Headers.ContentType?.ToString(),
                    responseBytes: responseBytes,
                    errorKind: "http_status",
                    exceptionMessage: null);

                errorLogged = true;
                response.EnsureSuccessStatusCode();
            }
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (!errorLogged)
            {
                WriteEnrollmentError(
                    errorKind: "timeout",
                    uri: uri,
                    cameraIpAddress: cameraIpAddress,
                    username: username,
                    appData: appData,
                    requestContentType: requestContentType,
                    requestPayload: formPayload,
                    requestExpectContinue: request.Headers.ExpectContinue,
                    statusCode: response?.StatusCode,
                    responseText: null,
                    responseBase64: null,
                    exceptionMessage: ex.Message,
                    parseError: null);

                WriteEnrollmentTrace(
                    uri: uri,
                    cameraIpAddress: cameraIpAddress,
                    username: username,
                    appData: appData,
                    requestContentType: requestContentType,
                    requestPayload: formPayload,
                    requestExpectContinue: request.Headers.ExpectContinue,
                    statusCode: response?.StatusCode,
                    responseContentType: null,
                    responseBytes: null,
                    errorKind: "timeout",
                    exceptionMessage: ex.Message);
            }

            throw new TimeoutException(
                $"Enrollment request timed out after {_options.TimeoutSeconds} seconds.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (!errorLogged)
            {
                WriteEnrollmentError(
                    errorKind: "exception",
                    uri: uri,
                    cameraIpAddress: cameraIpAddress,
                    username: username,
                    appData: appData,
                    requestContentType: requestContentType,
                    requestPayload: formPayload,
                    requestExpectContinue: request.Headers.ExpectContinue,
                    statusCode: response?.StatusCode,
                    responseText: null,
                    responseBase64: null,
                    exceptionMessage: ex.Message,
                    parseError: null);

                WriteEnrollmentTrace(
                    uri: uri,
                    cameraIpAddress: cameraIpAddress,
                    username: username,
                    appData: appData,
                    requestContentType: requestContentType,
                    requestPayload: formPayload,
                    requestExpectContinue: request.Headers.ExpectContinue,
                    statusCode: response?.StatusCode,
                    responseContentType: null,
                    responseBytes: null,
                    errorKind: "exception",
                    exceptionMessage: ex.Message);
            }
            throw;
        }

        if (response is null)
        {
            throw new InvalidOperationException("Enrollment CGI did not return a response.");
        }

        var responseContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        WriteEnrollmentTrace(
            uri: uri,
            cameraIpAddress: cameraIpAddress,
            username: username,
            appData: appData,
            requestContentType: requestContentType,
            requestPayload: formPayload,
            requestExpectContinue: request.Headers.ExpectContinue,
            statusCode: response.StatusCode,
            responseContentType: responseContentType,
            responseBytes: payload,
            errorKind: "ok",
            exceptionMessage: null);

        if (!TryParseEnrollmentResponse(payload, responseContentType, out var result, out var error))
        {
            _logger.LogWarning("Enrollment CGI parse failed: {Error}", error ?? "unknown_error");
            WriteEnrollmentError(
                errorKind: "parse_failed",
                uri: uri,
                cameraIpAddress: cameraIpAddress,
                username: username,
                appData: appData,
                requestContentType: requestContentType,
                requestPayload: formPayload,
                requestExpectContinue: request.Headers.ExpectContinue,
                statusCode: response.StatusCode,
                responseText: null,
                responseBase64: Convert.ToBase64String(payload),
                exceptionMessage: null,
                parseError: error);
            throw new InvalidOperationException("Failed to parse enrollment response.");
        }

        return result;
    }

    private sealed record AppDataBuildResult(
        string Base64,
        bool UsedTemplate,
        string Source,
        int JpegOffset,
        int OldJpegLength,
        int NewJpegLength,
        int PatchedCount);

    private AppDataBuildResult BuildAppData(byte[] imageJpeg)
    {
        var prepared = PrepareJpegForAppData(imageJpeg);
        var base64 = LightGuardAppData.BuildSAppDataFromJpegBytes(prepared);
        return new AppDataBuildResult(
            base64,
            false,
            "header+jpeg",
            24,
            0,
            prepared.Length,
            0);
    }

    private AppDataBuildResult BuildMergedAppData(byte[] imageJpeg, byte[] template, string source)
    {
        if (template.Length == 0)
        {
            return new AppDataBuildResult(
                Convert.ToBase64String(imageJpeg),
                false,
                "raw-jpeg",
                -1,
                0,
                imageJpeg.Length,
                0);
        }

        var jpegOffset = IndexOf(template, JpegMarker, 0);
        if (jpegOffset < 0)
        {
            _logger.LogWarning("Enrollment template missing JPEG marker, using raw JPEG.");
            return new AppDataBuildResult(
                Convert.ToBase64String(imageJpeg),
                false,
                "template-missing-jpeg",
                -1,
                0,
                imageJpeg.Length,
                0);
        }

        var headerLength = jpegOffset;
        var oldJpegLength = template.Length - jpegOffset;
        var newJpegLength = imageJpeg.Length;
        var header = new byte[headerLength];
        Buffer.BlockCopy(template, 0, header, 0, headerLength);

        var patched = PatchLengthFields(header, oldJpegLength, newJpegLength);
        if (patched == 0)
        {
            _logger.LogWarning("Enrollment template length fields not patched (source {Source}).", source);
        }

        var merged = new byte[header.Length + imageJpeg.Length];
        Buffer.BlockCopy(header, 0, merged, 0, header.Length);
        Buffer.BlockCopy(imageJpeg, 0, merged, header.Length, imageJpeg.Length);
        return new AppDataBuildResult(
            Convert.ToBase64String(merged),
            true,
            source,
            jpegOffset,
            oldJpegLength,
            newJpegLength,
            patched);
    }

    private bool TryLoadTemplateBytes(out byte[] template, out string source)
    {
        template = Array.Empty<byte>();
        source = string.Empty;

        if (!string.IsNullOrWhiteSpace(_options.AppDataTemplateFile))
        {
            try
            {
                var raw = File.ReadAllText(_options.AppDataTemplateFile, Encoding.UTF8);
                if (TryDecodeBase64Payload(raw, out template))
                {
                    source = _options.AppDataTemplateFile!;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read enrollment template file.");
            }
        }

        if (!string.IsNullOrWhiteSpace(_options.AppDataTemplateBase64) &&
            TryDecodeBase64Payload(_options.AppDataTemplateBase64, out template))
        {
            source = "config";
            return true;
        }

        return false;
    }

    private static bool TryDecodeBase64Payload(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (TryDecodeBase64(trimmed, out bytes))
        {
            return true;
        }

        var cleaned = new string(trimmed.Where(IsBase64Char).ToArray());
        if (cleaned.Length > 0 && TryDecodeBase64(cleaned, out bytes))
        {
            return true;
        }

        var match = Base64Token.Matches(trimmed)
            .OrderByDescending(m => m.Length)
            .FirstOrDefault();
        if (match is null)
        {
            return false;
        }

        return TryDecodeBase64(match.Value, out bytes);
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBase64Char(char value)
    {
        return char.IsLetterOrDigit(value) || value == '+' || value == '/' || value == '=';
    }

    private static int PatchLengthFields(byte[] header, int oldLength, int newLength)
    {
        var oldLe = BitConverter.GetBytes(oldLength);
        var newLe = BitConverter.GetBytes(newLength);
        var oldBe = new[] { oldLe[3], oldLe[2], oldLe[1], oldLe[0] };
        var newBe = new[] { newLe[3], newLe[2], newLe[1], newLe[0] };

        var patched = ReplaceAll(header, oldLe, newLe);
        patched += ReplaceAll(header, oldBe, newBe);
        return patched;
    }

    private static int ReplaceAll(byte[] buffer, byte[] oldBytes, byte[] newBytes)
    {
        var count = 0;
        for (var i = 0; i <= buffer.Length - oldBytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < oldBytes.Length; j++)
            {
                if (buffer[i + j] != oldBytes[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
            {
                continue;
            }

            Buffer.BlockCopy(newBytes, 0, buffer, i, newBytes.Length);
            count++;
            i += oldBytes.Length - 1;
        }

        return count;
    }

    private async Task<string> SafeReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

#pragma warning disable CA1416
    private byte[] PrepareJpegForAppData(byte[] imageJpeg)
    {
        if (_options.MaxJpegBytes <= 0 || imageJpeg.Length <= _options.MaxJpegBytes)
        {
            return imageJpeg;
        }

        if (!TryCompressJpeg(imageJpeg, _options.MaxJpegBytes, _options.MinResizeDimension, out var compressed))
        {
            return imageJpeg;
        }

        _logger.LogDebug(
            "Enrollment JPEG compressed from {Original} to {Compressed} bytes.",
            imageJpeg.Length,
            compressed.Length);
        return compressed;
    }

    private static bool TryCompressJpeg(byte[] sourceBytes, int maxBytes, int minDimension, out byte[] outputBytes)
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
            var minSize = Math.Max(16, minDimension);
            var firstPass = true;
            var qualitySteps = new long[] { 80, 70, 60, 50, 40, 30, 25 };

            while (firstPass || (width >= minSize && height >= minSize))
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
                return true;
            }
        }
        catch (Exception)
        {
            // Don't fail enrollment if compression fails.
            outputBytes = sourceBytes;
            return false;
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
        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
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

    private void WriteEnrollmentTrace(
        Uri uri,
        string cameraIpAddress,
        string username,
        AppDataBuildResult appData,
        string requestContentType,
        string requestPayload,
        bool? requestExpectContinue,
        HttpStatusCode? statusCode,
        string? responseContentType,
        byte[]? responseBytes,
        string errorKind,
        string? exceptionMessage)
    {
        if (string.IsNullOrWhiteSpace(_options.CgiTraceLogPath))
        {
            return;
        }

        var path = ResolveLogPath(_options.CgiTraceLogPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.Append('[').Append(DateTimeOffset.UtcNow.ToString("o")).Append("] enrollment_trace").AppendLine();
        builder.Append("error_kind=").Append(errorKind).AppendLine();
        builder.Append("camera_ip=").Append(cameraIpAddress).AppendLine();
        builder.Append("uri=").Append(uri).AppendLine();
        builder.Append("username=").Append(username).AppendLine();
        if (statusCode.HasValue)
        {
            builder.Append("status_code=").Append((int)statusCode.Value).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(exceptionMessage))
        {
            builder.Append("exception=").Append(exceptionMessage).AppendLine();
        }

        builder.Append("app_name=").Append(_options.AppName).AppendLine();
        builder.Append("app_data_type=").Append(_options.AppDataType).AppendLine();
        builder.Append("app_data_length=").Append(appData.Base64.Length).AppendLine();
        builder.Append("app_data_used_template=").Append(appData.UsedTemplate).AppendLine();
        builder.Append("app_data_source=").Append(appData.Source).AppendLine();
        builder.Append("request_method=POST").AppendLine();
        builder.Append("request_content_type=").Append(requestContentType).AppendLine();
        builder.Append("request_expect_continue=").Append(requestExpectContinue?.ToString() ?? "null").AppendLine();
        builder.Append("request_payload_length=").Append(requestPayload.Length).AppendLine();
        builder.Append("request_payload_url_encoded=").Append(requestPayload).AppendLine();
        builder.Append("request_payload_url_decoded=").Append(WebUtility.UrlDecode(requestPayload)).AppendLine();
        builder.Append("s_appData_base64=").Append(appData.Base64).AppendLine();
        builder.Append("response_content_type=").Append(responseContentType ?? string.Empty).AppendLine();
        if (responseBytes is { Length: > 0 })
        {
            builder.Append("response_payload_length=").Append(responseBytes.Length).AppendLine();
            builder.Append("response_base64=").Append(Convert.ToBase64String(responseBytes)).AppendLine();
            builder.Append("response_text_utf8=").Append(TryDecodeUtf8(responseBytes)).AppendLine();
        }
        else
        {
            builder.Append("response_payload_length=0").AppendLine();
        }

        builder.AppendLine("---");

        lock (TraceLogLock)
        {
            File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
        }
    }

    private static string TryDecodeUtf8(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void WriteEnrollmentError(
        string errorKind,
        Uri uri,
        string cameraIpAddress,
        string username,
        AppDataBuildResult appData,
        string requestContentType,
        string requestPayload,
        bool? requestExpectContinue,
        HttpStatusCode? statusCode,
        string? responseText,
        string? responseBase64,
        string? exceptionMessage,
        string? parseError)
    {
        if (string.IsNullOrWhiteSpace(_options.ErrorLogPath))
        {
            return;
        }

        var path = ResolveLogPath(_options.ErrorLogPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.Append('[').Append(DateTimeOffset.UtcNow.ToString("o")).Append("] enrollment_error").AppendLine();
        builder.Append("error_kind=").Append(errorKind).AppendLine();
        builder.Append("camera_ip=").Append(cameraIpAddress).AppendLine();
        builder.Append("uri=").Append(uri).AppendLine();
        builder.Append("username=").Append(username).AppendLine();
        if (statusCode.HasValue)
        {
            builder.Append("status_code=").Append((int)statusCode.Value).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(exceptionMessage))
        {
            builder.Append("exception=").Append(exceptionMessage).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(parseError))
        {
            builder.Append("parse_error=").Append(parseError).AppendLine();
        }

        builder.Append("app_name=").Append(_options.AppName).AppendLine();
        builder.Append("app_data_type=").Append(_options.AppDataType).AppendLine();
        builder.Append("app_data_length=").Append(appData.Base64.Length).AppendLine();
        builder.Append("app_data_used_template=").Append(appData.UsedTemplate).AppendLine();
        builder.Append("app_data_source=").Append(appData.Source).AppendLine();
        if (appData.UsedTemplate)
        {
            builder.Append("jpeg_offset=").Append(appData.JpegOffset).AppendLine();
            builder.Append("jpeg_length_old=").Append(appData.OldJpegLength).AppendLine();
            builder.Append("jpeg_length_new=").Append(appData.NewJpegLength).AppendLine();
            builder.Append("length_fields_patched=").Append(appData.PatchedCount).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(responseText))
        {
            builder.Append("response_text=").Append(responseText).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(responseBase64))
        {
            builder.Append("response_base64=").Append(responseBase64).AppendLine();
        }

        builder.Append("request_method=POST").AppendLine();
        builder.Append("request_content_type=").Append(requestContentType).AppendLine();
        builder.Append("request_expect_continue=").Append(requestExpectContinue?.ToString() ?? "null").AppendLine();
        builder.Append("request_payload_length=").Append(requestPayload.Length).AppendLine();
        builder.Append("request_payload=").Append(requestPayload).AppendLine();
        builder.Append("s_appData=").Append(appData.Base64).AppendLine();
        builder.AppendLine("---");

        lock (ErrorLogLock)
        {
            File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
        }
    }

    private static string ResolveLogPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), trimmed);
    }

    private Uri BuildCgiUri(string cameraIpAddress)
    {
        var (host, port, hasPort) = ParseHostPort(cameraIpAddress);
        var scheme = _options.UseHttps ? "https" : "http";
        var builder = new UriBuilder
        {
            Scheme = scheme,
            Host = host,
            Path = _options.CgiPath.StartsWith("/", StringComparison.Ordinal) ? _options.CgiPath : "/" + _options.CgiPath
        };

        builder.Port = hasPort && port != 554 ? port : _options.HttpPort;
        return builder.Uri;
    }

    private static (string Host, int Port, bool HasPort) ParseHostPort(string address)
    {
        var host = address;
        var port = 0;
        var hasPort = false;

        var parts = address.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && int.TryParse(parts[1], out var parsed))
        {
            host = parts[0];
            port = parsed;
            hasPort = true;
        }

        return (host, port, hasPort);
    }

    private static bool TryParseEnrollmentResponse(
        byte[] payload,
        string contentType,
        out FaceEnrollmentResult result,
        out string? error)
    {
        result = new FaceEnrollmentResult();
        error = null;

        var boundary = ParseBoundary(contentType) ?? ParseBoundaryFromPayload(payload);
        if (string.IsNullOrWhiteSpace(boundary))
        {
            error = "missing_boundary";
            return false;
        }

        var parts = SplitByBoundary(payload, boundary);
        if (parts.Count == 0)
        {
            error = "no_parts";
            return false;
        }

        byte[]? featureBytes = null;
        float? l2Norm = null;

        foreach (var part in parts)
        {
            if (!TryParsePart(part, out var name, out var body))
            {
                continue;
            }

            if (name.Equals("feat", StringComparison.OrdinalIgnoreCase))
            {
                featureBytes = body;
                continue;
            }

            if (name.Equals("l2norm", StringComparison.OrdinalIgnoreCase))
            {
                var text = Encoding.UTF8.GetString(body);
                l2Norm = ParseL2Norm(text);
            }
        }

        if (featureBytes is null || featureBytes.Length == 0)
        {
            error = "missing_feat";
            return false;
        }

        if (featureBytes.Length % sizeof(float) != 0)
        {
            error = "invalid_feat_length";
            return false;
        }

        var vector = MemoryMarshal.Cast<byte, float>(featureBytes).ToArray();
        var norm = l2Norm ?? ComputeL2Norm(vector);
        if (norm > 0.0001f)
        {
            var inv = 1f / norm;
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] *= inv;
            }
        }
        else
        {
            VectorMath.NormalizeInPlace(vector);
            norm = 1f;
        }

        result = new FaceEnrollmentResult
        {
            FeatureBytes = featureBytes,
            FeatureVector = vector,
            L2Norm = norm
        };
        return true;
    }

    private static string? ParseBoundary(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var idx = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var boundary = contentType.Substring(idx + "boundary=".Length).Trim();
        boundary = boundary.Trim('"');
        return string.IsNullOrWhiteSpace(boundary) ? null : boundary;
    }

    private static string? ParseBoundaryFromPayload(byte[] payload)
    {
        var lineEnd = IndexOf(payload, CrLf, 0);
        if (lineEnd <= 2)
        {
            return null;
        }

        var line = Encoding.ASCII.GetString(payload.AsSpan(0, lineEnd));
        if (!line.StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        return line.Substring(2).Trim();
    }

    private static List<byte[]> SplitByBoundary(byte[] payload, string boundary)
    {
        var boundaryBytes = Encoding.ASCII.GetBytes("--" + boundary);
        var segments = new List<byte[]>();
        var index = 0;

        while (true)
        {
            var start = IndexOf(payload, boundaryBytes, index);
            if (start < 0)
            {
                break;
            }

            start += boundaryBytes.Length;
            if (start + 1 < payload.Length && payload[start] == '-' && payload[start + 1] == '-')
            {
                break;
            }

            if (start + 1 < payload.Length && payload[start] == '\r' && payload[start + 1] == '\n')
            {
                start += 2;
            }

            var end = IndexOf(payload, boundaryBytes, start);
            if (end < 0)
            {
                end = payload.Length;
            }

            var length = end - start;
            if (length > 0)
            {
                var buffer = new byte[length];
                Buffer.BlockCopy(payload, start, buffer, 0, length);
                segments.Add(buffer);
            }

            index = end;
        }

        return segments;
    }

    private static bool TryParsePart(byte[] part, out string name, out byte[] body)
    {
        name = string.Empty;
        body = Array.Empty<byte>();

        var headerEnd = IndexOf(part, CrLfCrLf, 0);
        var bodyStart = 0;
        string headerText;

        if (headerEnd >= 0)
        {
            headerText = Encoding.ASCII.GetString(part.AsSpan(0, headerEnd));
            bodyStart = headerEnd + CrLfCrLf.Length;
        }
        else
        {
            var scan = 0;
            var lastLineEnd = -1;
            while (true)
            {
                var lineEnd = IndexOf(part, CrLf, scan);
                if (lineEnd < 0)
                {
                    return false;
                }

                var line = Encoding.ASCII.GetString(part.AsSpan(scan, lineEnd - scan));
                lastLineEnd = lineEnd;
                scan = lineEnd + CrLf.Length;

                if (line.Length == 0 || line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            if (lastLineEnd < 0)
            {
                return false;
            }

            headerText = Encoding.ASCII.GetString(part.AsSpan(0, lastLineEnd));
            bodyStart = scan;
        }

        if (bodyStart + 1 < part.Length && part[bodyStart] == '\r' && part[bodyStart + 1] == '\n')
        {
            bodyStart += 2;
        }
        var nameIndex = headerText.IndexOf("name=\"", StringComparison.OrdinalIgnoreCase);
        if (nameIndex >= 0)
        {
            nameIndex += "name=\"".Length;
            var nameEnd = headerText.IndexOf('"', nameIndex);
            if (nameEnd > nameIndex)
            {
                name = headerText.Substring(nameIndex, nameEnd - nameIndex);
            }
        }

        if (bodyStart >= part.Length)
        {
            return false;
        }

        var length = part.Length - bodyStart;
        if (length <= 0)
        {
            return false;
        }

        var bodyBytes = new byte[length];
        Buffer.BlockCopy(part, bodyStart, bodyBytes, 0, length);
        body = TrimTrailingCrlf(bodyBytes);
        return true;
    }

    private static byte[] TrimTrailingCrlf(byte[] body)
    {
        var length = body.Length;
        while (length >= 2 && body[length - 2] == '\r' && body[length - 1] == '\n')
        {
            length -= 2;
        }

        if (length == body.Length)
        {
            return body;
        }

        var trimmed = new byte[length];
        Buffer.BlockCopy(body, 0, trimmed, 0, length);
        return trimmed;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int startIndex)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        for (var i = startIndex; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static float? ParseL2Norm(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var startTag = "<l2norm>";
        var endTag = "</l2norm>";
        var startIndex = xml.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += startTag.Length;
        var endIndex = xml.IndexOf(endTag, startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex <= startIndex)
        {
            return null;
        }

        var base64 = xml.Substring(startIndex, endIndex - startIndex).Trim();
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length != sizeof(float))
            {
                return null;
            }

            return MemoryMarshal.Cast<byte, float>(bytes)[0];
        }
        catch
        {
            return null;
        }
    }

    private static float ComputeL2Norm(float[] vector)
    {
        if (vector.Length == 0)
        {
            return 0f;
        }

        double sum = 0;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        return (float)Math.Sqrt(sum);
    }
}

