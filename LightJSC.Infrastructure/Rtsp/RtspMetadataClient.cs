using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using System.Text;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightJSC.Infrastructure.Rtsp;

public sealed class RtspMetadataClient : IRtspMetadataClient
{
    private readonly RtspOptions _options;
    private readonly ILogger<RtspMetadataClient> _logger;

    public RtspMetadataClient(IOptions<RtspOptions> options, ILogger<RtspMetadataClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

        public async IAsyncEnumerable<string> StreamMetadataAsync(Uri rtspUri, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var backoffSeconds = _options.ReconnectMinSeconds;

            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting RTSP metadata session for {RtspUri}. Backoff={BackoffSeconds}s", rtspUri, backoffSeconds);
                using var session = new RtspSession(rtspUri, _options, _logger);
                var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true
                });

                var sessionTask = Task.Run(async () =>
                {
                    try
                    {
                        await session.ConnectAsync(cancellationToken);
                        backoffSeconds = _options.ReconnectMinSeconds;

                        await foreach (var message in session.ReadMetadataAsync(cancellationToken))
                        {
                            await channel.Writer.WriteAsync(message, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (TimeoutException ex)
                    {
                        _logger.LogWarning(ex, "RTSP metadata receive timeout for {RtspUri}", rtspUri);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RTSP metadata stream failed for {RtspUri}; reconnecting in {BackoffSeconds}s", rtspUri, backoffSeconds);
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                }, cancellationToken);

            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }

            await sessionTask;

            var delay = TimeSpan.FromSeconds(Math.Min(backoffSeconds, _options.ReconnectMaxSeconds));
            backoffSeconds = Math.Min(backoffSeconds * 2, _options.ReconnectMaxSeconds);
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    public async Task<bool> TestAsync(Uri rtspUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var connected = false;
        var metadataReceived = false;

        try
        {
            using var session = new RtspSession(rtspUri, _options, _logger);
            await session.ConnectAsync(timeoutCts.Token);
            connected = true;
            await foreach (var _ in session.ReadMetadataAsync(timeoutCts.Token))
            {
                metadataReceived = true;
                return true;
            }
        }
        catch (OperationCanceledException) when (connected)
        {
            _logger.LogInformation("RTSP test connected but no metadata within timeout for {RtspUri}", rtspUri);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RTSP test failed for {RtspUri}", rtspUri);
            return false;
        }

        return connected && metadataReceived;
    }

    private sealed class RtspSession : IDisposable
    {
        private readonly Uri _rtspUri;
        private readonly RtspOptions _options;
        private readonly ILogger _logger;
        private readonly RtspCredentials? _credentials;
        private RtspAuthState? _authState;
        private string? _trackUri;
        private string? _playUri;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _cseq = 1;
        private string? _sessionId;
        private Timer? _keepAliveTimer;
        private int _rtpChannel = 0;
        private int _rtcpChannel = 1;

        public RtspSession(Uri rtspUri, RtspOptions options, ILogger logger)
        {
            _rtspUri = rtspUri;
            _options = options;
            _logger = logger;
            _credentials = TryParseCredentials(rtspUri);
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            _client = new TcpClient();
            var port = _rtspUri.Port > 0 ? _rtspUri.Port : 554;
            var connectTask = _client.ConnectAsync(_rtspUri.Host, port);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

            var completed = await Task.WhenAny(connectTask, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));
            if (completed != connectTask)
            {
                throw new TimeoutException("RTSP connection timeout.");
            }

            await connectTask;
            _stream = _client.GetStream();
            _stream.ReadTimeout = _options.ReceiveTimeoutSeconds * 1000;

            await SendOptionsAsync(cancellationToken);
            var sdp = await SendDescribeAsync(cancellationToken);
            var selection = SdpParser.SelectMetadataTrack(_rtspUri, sdp);
            _trackUri = selection.TrackUri;
            _playUri = selection.PlayUri;
            await SendSetupAsync(selection.TrackUri, cancellationToken);
            await SendPlayAsync(cancellationToken);

            _keepAliveTimer = new Timer(async _ =>
            {
                try
                {
                    await SendKeepAliveAsync(CancellationToken.None);
                }
                catch
                {
                    // Ignore keep-alive failures; read loop handles reconnect.
                }
            }, null, TimeSpan.FromSeconds(_options.KeepAliveSeconds), TimeSpan.FromSeconds(_options.KeepAliveSeconds));
        }

        public async IAsyncEnumerable<string> ReadMetadataAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_stream is null)
            {
                yield break;
            }

            var receiveTimeout = TimeSpan.FromSeconds(_options.ReceiveTimeoutSeconds);
            var buffer = new MemoryStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                var first = await ReadByteAsync(_stream, receiveTimeout, cancellationToken);
                if (first == -1)
                {
                    yield break;
                }

                if (first == '$')
                {
                    var channel = await ReadByteAsync(_stream, receiveTimeout, cancellationToken);
                    var lengthBytes = await ReadExactlyAsync(_stream, 2, receiveTimeout, cancellationToken);
                    var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
                    var packet = await ReadExactlyAsync(_stream, length, receiveTimeout, cancellationToken);

                    if (channel == _rtpChannel)
                    {
                        if (TryExtractPayload(packet, out var payload, out _))
                        {
                            if (buffer.Length + payload.Length > _options.MaxMetadataFrameBytes)
                            {
                                _logger.LogWarning(
                                    "RTSP metadata buffer exceeded {MaxBytes} bytes for {RtspUri}; dropping buffer.",
                                    _options.MaxMetadataFrameBytes,
                                    _rtspUri);
                                buffer.SetLength(0);
                                continue;
                            }

                            buffer.Write(payload, 0, payload.Length);

                            foreach (var message in DrainMessages(buffer))
                            {
                                yield return message;
                            }
                        }
                    }
                }
                else
                {
                    await ReadRtspResponseAsync(_stream, (byte)first, receiveTimeout, cancellationToken);
                }
            }
        }

        private async Task SendOptionsAsync(CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync("OPTIONS", _rtspUri.ToString(), cancellationToken);
            EnsureSuccess(response);
        }

        private async Task SendKeepAliveAsync(CancellationToken cancellationToken)
        {
            var uri = GetKeepAliveUri();
            await SendRequestNoResponseAsync("OPTIONS", uri, cancellationToken);
        }

        private async Task<string> SendDescribeAsync(CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync("DESCRIBE", _rtspUri.ToString(), cancellationToken, new Dictionary<string, string>
            {
                ["Accept"] = "application/sdp"
            });

            EnsureSuccess(response);
            return response.Body ?? string.Empty;
        }

        private async Task SendSetupAsync(string trackUri, CancellationToken cancellationToken)
        {
            var response = await SendRequestAsync("SETUP", trackUri, cancellationToken, new Dictionary<string, string>
            {
                ["Transport"] = "RTP/AVP/TCP;unicast;interleaved=0-1"
            });

            EnsureSuccess(response);
            if (response.Headers.TryGetValue("Session", out var sessionHeader))
            {
                _sessionId = sessionHeader.Split(';')[0];
            }

            if (response.Headers.TryGetValue("Transport", out var transportHeader)
                && TryParseInterleavedChannels(transportHeader, out var rtpChannel, out var rtcpChannel))
            {
                _rtpChannel = rtpChannel;
                _rtcpChannel = rtcpChannel;
            }
        }

        private async Task SendPlayAsync(CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                ["Range"] = "npt=0.000-"
            };

            var playUri = _playUri ?? _rtspUri.ToString();
            var response = await SendRequestAsync("PLAY", playUri, cancellationToken, headers);
            if ((response.StatusCode == 400 || response.StatusCode == 404) && !string.IsNullOrWhiteSpace(_trackUri)
                && !string.Equals(_trackUri, playUri, StringComparison.OrdinalIgnoreCase))
            {
                response = await SendRequestAsync("PLAY", _trackUri, cancellationToken, headers);
            }

            EnsureSuccess(response);
        }

        private string GetKeepAliveUri()
        {
            if (!string.IsNullOrWhiteSpace(_playUri))
            {
                return _playUri;
            }

            if (!string.IsNullOrWhiteSpace(_trackUri))
            {
                return _trackUri;
            }

            return _rtspUri.ToString();
        }

        private async Task<RtspResponse> SendRequestAsync(string method, string uri, CancellationToken cancellationToken, Dictionary<string, string>? headers = null)
        {
            var response = await SendRequestInternalAsync(method, uri, cancellationToken, headers);
            if (response.StatusCode == 401 && _credentials is not null && TryUpdateAuthState(response.Headers))
            {
                response = await SendRequestInternalAsync(method, uri, cancellationToken, headers);
            }

            return response;
        }

        private async Task<RtspResponse> SendRequestInternalAsync(string method, string uri, CancellationToken cancellationToken, Dictionary<string, string>? headers)
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("RTSP stream not initialized.");
            }

            var builder = new StringBuilder();
            builder.Append(method).Append(' ').Append(uri).Append(" RTSP/1.0\r\n");
            builder.Append("CSeq: ").Append(_cseq++).Append("\r\n");
            builder.Append("User-Agent: LightJSC\r\n");

            var auth = BuildAuthorizationHeader(method, uri);
            if (!string.IsNullOrWhiteSpace(auth))
            {
                builder.Append("Authorization: ").Append(auth).Append("\r\n");
            }

            if (!string.IsNullOrWhiteSpace(_sessionId) && !HasHeader(headers, "Session"))
            {
                builder.Append("Session: ").Append(_sessionId).Append("\r\n");
            }

            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    builder.Append(key).Append(": ").Append(value).Append("\r\n");
                }
            }

            builder.Append("\r\n");

            var bytes = Encoding.ASCII.GetBytes(builder.ToString());
            await WriteAsync(bytes, cancellationToken);
            var receiveTimeout = TimeSpan.FromSeconds(_options.ReceiveTimeoutSeconds);
            return await ReadRtspResponseAsync(_stream, receiveTimeout, cancellationToken);
        }

        private async Task SendRequestNoResponseAsync(string method, string uri, CancellationToken cancellationToken)
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("RTSP stream not initialized.");
            }

            var builder = new StringBuilder();
            builder.Append(method).Append(' ').Append(uri).Append(" RTSP/1.0\r\n");
            builder.Append("CSeq: ").Append(_cseq++).Append("\r\n");
            builder.Append("User-Agent: LightJSC\r\n");

            var auth = BuildAuthorizationHeader(method, uri);
            if (!string.IsNullOrWhiteSpace(auth))
            {
                builder.Append("Authorization: ").Append(auth).Append("\r\n");
            }

            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                builder.Append("Session: ").Append(_sessionId).Append("\r\n");
            }

            builder.Append("\r\n");

            var bytes = Encoding.ASCII.GetBytes(builder.ToString());
            await WriteAsync(bytes, cancellationToken);
        }

        private async Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            if (_stream is null)
            {
                throw new InvalidOperationException("RTSP stream not initialized.");
            }

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private static bool HasHeader(Dictionary<string, string>? headers, string name)
        {
            if (headers is null)
            {
                return false;
            }

            foreach (var key in headers.Keys)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string? BuildAuthorizationHeader(string method, string uri)
        {
            if (_credentials is null)
            {
                return null;
            }

            if (_authState?.Scheme == RtspAuthScheme.Digest && !string.IsNullOrWhiteSpace(_authState.Nonce) && !string.IsNullOrWhiteSpace(_authState.Realm))
            {
                return BuildDigestAuthorizationHeader(method, uri, _credentials, _authState);
            }

            return BuildBasicAuthorizationHeader(_credentials);
        }

        private static string BuildBasicAuthorizationHeader(RtspCredentials credentials)
        {
            var userInfo = $"{credentials.UserName}:{credentials.Password}";
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(userInfo));
            return "Basic " + encoded;
        }

        private bool TryUpdateAuthState(Dictionary<string, string> headers)
        {
            if (_credentials is null)
            {
                return false;
            }

            if (!headers.TryGetValue("WWW-Authenticate", out var challenge) || string.IsNullOrWhiteSpace(challenge))
            {
                return false;
            }

            var digestIndex = challenge.IndexOf("Digest", StringComparison.OrdinalIgnoreCase);
            if (digestIndex >= 0)
            {
                var digestPart = challenge.Substring(digestIndex + "Digest".Length).Trim();
                var parameters = ParseAuthParameters(digestPart);
                if (!parameters.TryGetValue("nonce", out var nonce) || !parameters.TryGetValue("realm", out var realm))
                {
                    return false;
                }

                _authState = new RtspAuthState(RtspAuthScheme.Digest)
                {
                    Realm = realm,
                    Nonce = nonce,
                    Algorithm = parameters.TryGetValue("algorithm", out var algorithm) ? algorithm : null,
                    Qop = NormalizeQop(parameters.TryGetValue("qop", out var qop) ? qop : null),
                    Opaque = parameters.TryGetValue("opaque", out var opaque) ? opaque : null,
                    NonceCount = 0,
                    Cnonce = null
                };

                return true;
            }

            var basicIndex = challenge.IndexOf("Basic", StringComparison.OrdinalIgnoreCase);
            if (basicIndex >= 0)
            {
                _authState = new RtspAuthState(RtspAuthScheme.Basic);
                return true;
            }

            return false;
        }

        private static Dictionary<string, string> ParseAuthParameters(string input)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var i = 0;

            while (i < input.Length)
            {
                while (i < input.Length && (input[i] == ',' || char.IsWhiteSpace(input[i])))
                {
                    i++;
                }

                if (i >= input.Length)
                {
                    break;
                }

                var keyStart = i;
                while (i < input.Length && input[i] != '=' && input[i] != ',')
                {
                    i++;
                }

                if (i >= input.Length || input[i] != '=')
                {
                    break;
                }

                var key = input.Substring(keyStart, i - keyStart).Trim();
                i++; // skip '='

                while (i < input.Length && char.IsWhiteSpace(input[i]))
                {
                    i++;
                }

                string value;
                if (i < input.Length && input[i] == '"')
                {
                    i++;
                    var valueStart = i;
                    while (i < input.Length && input[i] != '"')
                    {
                        i++;
                    }

                    value = input.Substring(valueStart, i - valueStart);
                    if (i < input.Length && input[i] == '"')
                    {
                        i++;
                    }
                }
                else
                {
                    var valueStart = i;
                    while (i < input.Length && input[i] != ',')
                    {
                        i++;
                    }

                    value = input.Substring(valueStart, i - valueStart).Trim();
                }

                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private static string? NormalizeQop(string? qop)
        {
            if (string.IsNullOrWhiteSpace(qop))
            {
                return null;
            }

            var raw = qop.Trim().Trim('"');
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (string.Equals(part, "auth", StringComparison.OrdinalIgnoreCase))
                {
                    return "auth";
                }
            }

            return parts.Length > 0 ? parts[0] : raw;
        }

        private static string BuildDigestAuthorizationHeader(string method, string uri, RtspCredentials credentials, RtspAuthState state)
        {
            var realm = state.Realm ?? string.Empty;
            var nonce = state.Nonce ?? string.Empty;
            var algorithm = string.IsNullOrWhiteSpace(state.Algorithm) ? "MD5" : state.Algorithm!;
            var qop = state.Qop;

            if (string.IsNullOrWhiteSpace(state.Cnonce))
            {
                state.Cnonce = GenerateCnonce();
            }

            state.NonceCount++;
            var nc = state.NonceCount.ToString("x8");

            var ha1 = ComputeMd5($"{credentials.UserName}:{realm}:{credentials.Password}");
            if (string.Equals(algorithm, "MD5-sess", StringComparison.OrdinalIgnoreCase))
            {
                ha1 = ComputeMd5($"{ha1}:{nonce}:{state.Cnonce}");
            }

            var ha2 = ComputeMd5($"{method}:{uri}");
            var response = !string.IsNullOrWhiteSpace(qop)
                ? ComputeMd5($"{ha1}:{nonce}:{nc}:{state.Cnonce}:{qop}:{ha2}")
                : ComputeMd5($"{ha1}:{nonce}:{ha2}");

            var header = new StringBuilder();
            header.Append("Digest ");
            header.Append("username=\"").Append(credentials.UserName).Append("\", ");
            header.Append("realm=\"").Append(realm).Append("\", ");
            header.Append("nonce=\"").Append(nonce).Append("\", ");
            header.Append("uri=\"").Append(uri).Append("\", ");
            header.Append("response=\"").Append(response).Append("\"");

            if (!string.IsNullOrWhiteSpace(state.Opaque))
            {
                header.Append(", opaque=\"").Append(state.Opaque).Append("\"");
            }

            if (!string.IsNullOrWhiteSpace(state.Algorithm))
            {
                header.Append(", algorithm=").Append(state.Algorithm);
            }

            if (!string.IsNullOrWhiteSpace(qop))
            {
                header.Append(", qop=").Append(qop);
                header.Append(", nc=").Append(nc);
                header.Append(", cnonce=\"").Append(state.Cnonce).Append("\"");
            }

            return header.ToString();
        }

        private static string ComputeMd5(string value)
        {
            var hash = MD5.HashData(Encoding.ASCII.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string GenerateCnonce()
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static RtspCredentials? TryParseCredentials(Uri uri)
        {
            if (string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                return null;
            }

            var parts = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(parts[0]);
            var password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            return new RtspCredentials(user, password);
        }

        private static async Task<RtspResponse> ReadRtspResponseAsync(NetworkStream stream, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var headerBytes = await ReadUntilAsync(stream, "\r\n\r\n", timeout, cancellationToken);
            var headerText = Encoding.ASCII.GetString(headerBytes);
            return await ParseResponseAsync(stream, headerText, timeout, cancellationToken);
        }

        private static async Task<RtspResponse> ReadRtspResponseAsync(NetworkStream stream, byte firstByte, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var headerBytes = await ReadUntilAsync(stream, "\r\n\r\n", timeout, cancellationToken, firstByte);
            var headerText = Encoding.ASCII.GetString(headerBytes);
            return await ParseResponseAsync(stream, headerText, timeout, cancellationToken);
        }

        private static async Task<RtspResponse> ParseResponseAsync(NetworkStream stream, string headerText, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var statusLine = lines.Length > 0 ? lines[0] : string.Empty;
            var statusParts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var status = statusParts.Length >= 2 && int.TryParse(statusParts[1], out var code) ? code : 0;

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var idx = lines[i].IndexOf(':');
                if (idx <= 0)
                {
                    continue;
                }

                var key = lines[i].Substring(0, idx).Trim();
                var value = lines[i].Substring(idx + 1).Trim();
                headers[key] = value;
            }

            var body = string.Empty;
            if (headers.TryGetValue("Content-Length", out var lengthText) && int.TryParse(lengthText, out var length) && length > 0)
            {
                var bytes = await ReadExactlyAsync(stream, length, timeout, cancellationToken);
                body = Encoding.UTF8.GetString(bytes);
            }

            return new RtspResponse(status, headers, body);
        }

        private static void EnsureSuccess(RtspResponse response)
        {
            if (response.StatusCode >= 200 && response.StatusCode < 300)
            {
                return;
            }

            throw new InvalidOperationException($"RTSP request failed with status {response.StatusCode}.");
        }

        private static async Task<int> ReadByteAsync(NetworkStream stream, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var buffer = new byte[1];
            var read = await ReadWithTimeoutAsync(stream, buffer, 0, 1, timeout, cancellationToken);
            return read == 0 ? -1 : buffer[0];
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var buffer = new byte[length];
            var offset = 0;
            while (offset < length)
            {
                var read = await ReadWithTimeoutAsync(stream, buffer, offset, length - offset, timeout, cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("RTSP stream ended.");
                }

                offset += read;
            }

            return buffer;
        }

        private static async Task<byte[]> ReadUntilAsync(NetworkStream stream, string delimiter, TimeSpan timeout, CancellationToken cancellationToken, byte? firstByte = null)
        {
            var delimiterBytes = Encoding.ASCII.GetBytes(delimiter);
            var buffer = new List<byte>();
            if (firstByte.HasValue)
            {
                buffer.Add(firstByte.Value);
            }

            while (true)
            {
                var b = await ReadByteAsync(stream, timeout, cancellationToken);
                if (b == -1)
                {
                    break;
                }

                buffer.Add((byte)b);
                if (buffer.Count >= delimiterBytes.Length)
                {
                    var match = true;
                    for (var i = 0; i < delimiterBytes.Length; i++)
                    {
                        if (buffer[buffer.Count - delimiterBytes.Length + i] != delimiterBytes[i])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        break;
                    }
                }
            }

            return buffer.ToArray();
        }

        private static async Task<int> ReadWithTimeoutAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (timeout <= TimeSpan.Zero)
            {
                return await stream.ReadAsync(buffer, offset, count, cancellationToken);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                return await stream.ReadAsync(buffer, offset, count, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("RTSP receive timeout.");
            }
        }

        private List<string> DrainMessages(MemoryStream buffer)
        {
            var messages = new List<string>();
            if (buffer.Length == 0)
            {
                return messages;
            }

            var text = Encoding.UTF8.GetString(buffer.ToArray());
            if (string.IsNullOrWhiteSpace(text))
            {
                buffer.SetLength(0);
                return messages;
            }

            if (text.IndexOf('<', StringComparison.Ordinal) >= 0)
            {
                var startIndex = 0;
                while (true)
                {
                    if (!TryFindMetadataEnd(text, startIndex, out var endIndex, out var endLength))
                    {
                        break;
                    }

                    var message = text.Substring(startIndex, endIndex + endLength - startIndex).Trim();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        messages.Add(message);
                    }

                    startIndex = endIndex + endLength;
                }

                var remainder = startIndex < text.Length ? text.Substring(startIndex) : string.Empty;
                buffer.SetLength(0);
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    var remainingBytes = Encoding.UTF8.GetBytes(remainder);
                    buffer.Write(remainingBytes, 0, remainingBytes.Length);
                }

                return messages;
            }

            if (LooksLikeKeyValue(text))
            {
                messages.Add(text.Trim());
                buffer.SetLength(0);
            }

            return messages;
        }

        private static bool LooksLikeKeyValue(string text)
        {
            if (text.IndexOf('<', StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var hasFeature = text.IndexOf("FeatureValue=", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!hasFeature)
            {
                return false;
            }

            var hasL2 = text.IndexOf("L2Norm=", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasStartTime = text.IndexOf("start-time=", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasVersion = text.IndexOf("feature-value-version", StringComparison.OrdinalIgnoreCase) >= 0;

            return hasL2 || hasStartTime || hasVersion;
        }

        private static bool TryFindMetadataEnd(string text, int startIndex, out int endIndex, out int endLength)
        {
            const string metadataStream = "</tt:MetadataStream>";
            const string metaDataStream = "</tt:MetaDataStream>";

            var idx1 = text.IndexOf(metadataStream, startIndex, StringComparison.OrdinalIgnoreCase);
            var idx2 = text.IndexOf(metaDataStream, startIndex, StringComparison.OrdinalIgnoreCase);

            if (idx1 < 0 && idx2 < 0)
            {
                endIndex = -1;
                endLength = 0;
                return false;
            }

            if (idx2 < 0 || (idx1 >= 0 && idx1 < idx2))
            {
                endIndex = idx1;
                endLength = metadataStream.Length;
                return true;
            }

            endIndex = idx2;
            endLength = metaDataStream.Length;
            return true;
        }

        private static bool TryParseInterleavedChannels(string transportHeader, out int rtpChannel, out int rtcpChannel)
        {
            rtpChannel = 0;
            rtcpChannel = 1;
            if (string.IsNullOrWhiteSpace(transportHeader))
            {
                return false;
            }

            foreach (var part in transportHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith("interleaved=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = part.Substring("interleaved=".Length);
                var channels = value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (channels.Length == 0)
                {
                    continue;
                }

                if (!int.TryParse(channels[0], out rtpChannel))
                {
                    return false;
                }

                if (channels.Length > 1 && int.TryParse(channels[1], out var parsedRtcp))
                {
                    rtcpChannel = parsedRtcp;
                }

                return true;
            }

            return false;
        }

        private static bool TryExtractPayload(byte[] packet, out byte[] payload, out bool marker)
        {
            payload = Array.Empty<byte>();
            marker = false;
            if (packet.Length < 12)
            {
                return false;
            }

            var vpxcc = packet[0];
            var cc = vpxcc & 0x0F;
            var hasExtension = (vpxcc & 0x10) != 0;
            marker = (packet[1] & 0x80) != 0;

            var headerLength = 12 + (cc * 4);
            if (packet.Length < headerLength)
            {
                return false;
            }

            if (hasExtension)
            {
                if (packet.Length < headerLength + 4)
                {
                    return false;
                }

                var extLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(headerLength + 2, 2));
                headerLength += 4 + (extLength * 4);
            }

            if (packet.Length <= headerLength)
            {
                return false;
            }

            payload = new byte[packet.Length - headerLength];
            Buffer.BlockCopy(packet, headerLength, payload, 0, payload.Length);
            return true;
        }

        public void Dispose()
        {
            _keepAliveTimer?.Dispose();
            _stream?.Dispose();
            _client?.Dispose();
            _writeLock.Dispose();
        }

        private sealed record RtspCredentials(string UserName, string Password);

        private enum RtspAuthScheme
        {
            Basic,
            Digest
        }

        private sealed class RtspAuthState
        {
            public RtspAuthState(RtspAuthScheme scheme)
            {
                Scheme = scheme;
            }

            public RtspAuthScheme Scheme { get; }
            public string? Realm { get; set; }
            public string? Nonce { get; set; }
            public string? Algorithm { get; set; }
            public string? Qop { get; set; }
            public string? Opaque { get; set; }
            public int NonceCount { get; set; }
            public string? Cnonce { get; set; }
        }
    }

    private sealed record RtspResponse(int StatusCode, Dictionary<string, string> Headers, string? Body);

    private static class SdpParser
    {
        internal static SdpSelection SelectMetadataTrack(Uri baseUri, string sdp)
        {
            var session = Parse(sdp);
            var media = session.Media;
            var selected = media.FirstOrDefault(m => m.RtpMap?.Contains("onvif.metadata", StringComparison.OrdinalIgnoreCase) == true)
                ?? media.FirstOrDefault(m => string.Equals(m.MediaType, "application", StringComparison.OrdinalIgnoreCase))
                ?? media.FirstOrDefault();

            if (selected is null || string.IsNullOrWhiteSpace(selected.Control))
            {
                throw new InvalidOperationException("No RTSP metadata track found in SDP.");
            }

            var trackUri = ResolveControlUri(baseUri, selected.Control);
            var playUri = ResolvePlayUri(baseUri, session.SessionControl);
            return new SdpSelection(trackUri, playUri);
        }

        private static SdpSession Parse(string sdp)
        {
            var media = new List<SdpMedia>();
            SdpMedia? current = null;
            string? sessionControl = null;

            foreach (var line in sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("m=", StringComparison.Ordinal))
                {
                    var parts = line.Substring(2).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        current = new SdpMedia
                        {
                            MediaType = parts[0],
                            Format = parts[3]
                        };
                        media.Add(current);
                    }
                }
                else if (line.StartsWith("a=control:", StringComparison.Ordinal) && current is not null)
                {
                    current.Control = line.Substring("a=control:".Length).Trim();
                }
                else if (line.StartsWith("a=control:", StringComparison.Ordinal))
                {
                    sessionControl = line.Substring("a=control:".Length).Trim();
                }
                else if (line.StartsWith("a=rtpmap:", StringComparison.Ordinal) && current is not null)
                {
                    current.RtpMap = line.Substring("a=rtpmap:".Length).Trim();
                }
            }

            return new SdpSession(media, sessionControl);
        }

        private static string ResolvePlayUri(Uri baseUri, string? sessionControl)
        {
            if (string.IsNullOrWhiteSpace(sessionControl) || sessionControl == "*")
            {
                return baseUri.ToString();
            }

            return ResolveControlUri(baseUri, sessionControl);
        }

        private static string ResolveControlUri(Uri baseUri, string control)
        {
            if (control.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
            {
                return control;
            }

            if (control == "*")
            {
                return baseUri.ToString();
            }

            var baseString = baseUri.ToString();
            if (!baseString.EndsWith("/", StringComparison.Ordinal))
            {
                baseString += "/";
            }

            return baseString + control.TrimStart('/');
        }

        private sealed record SdpSession(IReadOnlyList<SdpMedia> Media, string? SessionControl);

        internal sealed record SdpSelection(string TrackUri, string PlayUri);

        private sealed class SdpMedia
        {
            public string? MediaType { get; set; }
            public string? Format { get; set; }
            public string? Control { get; set; }
            public string? RtpMap { get; set; }
        }
    }
}

