using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Xml.Linq;

namespace LightJSC.Infrastructure.Discovery;

public sealed class OnvifDiscoveryService : ICameraDiscoveryService
{
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 3702;
    private const int MaxFallbackTargets = 1024;

    private static readonly XNamespace WsAddressingNs = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
    private static readonly XNamespace WsDiscoveryNs = "http://schemas.xmlsoap.org/ws/2005/04/discovery";

    private readonly ILogger<OnvifDiscoveryService> _logger;

    public OnvifDiscoveryService(ILogger<OnvifDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveredCamera>> DiscoverAsync(
        TimeSpan timeout,
        IPAddress? ipStart,
        IPAddress? ipEnd,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(4);
        }

        var messageId = "uuid:" + Guid.NewGuid().ToString("D");
        var probe = BuildProbe(messageId);
        var payload = Encoding.UTF8.GetBytes(probe);

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.ReceiveTimeout = Math.Max(200, (int)timeout.TotalMilliseconds);
        udp.MulticastLoopback = false;

        var results = new Dictionary<string, DiscoveredCamera>(StringComparer.OrdinalIgnoreCase);

        async Task ReceiveResponsesAsync(DateTime deadline)
        {
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                try
                {
                    var received = await udp.ReceiveAsync().WaitAsync(remaining, cancellationToken);
                    foreach (var camera in ParseResponse(received.Buffer, received.RemoteEndPoint.Address))
                    {
                        var key = camera.IpAddress ?? camera.DeviceId ?? Guid.NewGuid().ToString("N");
                        if (!results.ContainsKey(key))
                        {
                            results[key] = camera;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // UDP unicast scans can trigger ICMP resets from non-ONVIF hosts; ignore and keep listening.
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse ONVIF discovery response.");
                }
            }
        }

        var useUnicast = ipStart is not null && ipEnd is not null;
        if (useUnicast)
        {
            if (!TryToUInt32(ipStart!, out var startValue)
                || !TryToUInt32(ipEnd!, out var endValue)
                || startValue > endValue)
            {
                _logger.LogWarning("Invalid IP range for discovery; falling back to multicast.");
                useUnicast = false;
            }
            else
            {
                foreach (var address in ExpandIpv4Range(startValue, endValue))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var endpoint = new IPEndPoint(address, MulticastPort);
                    await udp.SendAsync(payload, payload.Length, endpoint);
                }
            }
        }

        if (!useUnicast)
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), MulticastPort);
            await udp.SendAsync(payload, payload.Length, endpoint);
        }

        await ReceiveResponsesAsync(DateTime.UtcNow + timeout);

        if (results.Count == 0 && ipStart is null && ipEnd is null)
        {
            var ranges = GetLocalIpv4Ranges(MaxFallbackTargets);
            if (ranges.Count > 0)
            {
                foreach (var (startValue, endValue) in ranges)
                {
                    foreach (var address in ExpandIpv4Range(startValue, endValue))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var endpoint = new IPEndPoint(address, MulticastPort);
                        await udp.SendAsync(payload, payload.Length, endpoint);
                    }
                }

                await ReceiveResponsesAsync(DateTime.UtcNow + timeout);
            }
        }

        return results.Values
            .Where(IsIproCamera)
            .OrderBy(camera => camera.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildProbe(string messageId)
    {
        return $"""
<?xml version="1.0" encoding="utf-8"?>
<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
            xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
            xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
            xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
  <e:Header>
    <w:MessageID>{messageId}</w:MessageID>
    <w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
    <w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
  </e:Header>
  <e:Body>
    <d:Probe>
      <d:Types>dn:NetworkVideoTransmitter</d:Types>
    </d:Probe>
  </e:Body>
</e:Envelope>
""";
    }

    private static IEnumerable<DiscoveredCamera> ParseResponse(byte[] payload, IPAddress remoteAddress)
    {
        var xml = Encoding.UTF8.GetString(payload);
        var doc = XDocument.Parse(xml);
        var matches = doc.Descendants(WsDiscoveryNs + "ProbeMatch");

        foreach (var match in matches)
        {
            var xaddrs = match.Element(WsDiscoveryNs + "XAddrs")?.Value ?? string.Empty;
            var xaddr = xaddrs.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var scopes = match.Element(WsDiscoveryNs + "Scopes")?.Value ?? string.Empty;
            var deviceId = match
                .Element(WsAddressingNs + "EndpointReference")
                ?.Element(WsAddressingNs + "Address")
                ?.Value;

            var ipAddress = ExtractHost(xaddr) ?? remoteAddress.ToString();
            var name = GetScopeValue(scopes, "name");
            var model = GetScopeValue(scopes, "hardware") ?? GetScopeValue(scopes, "model");
            var mac = GetScopeValue(scopes, "mac");
            var series = ResolveCameraSeries(model);

            yield return new DiscoveredCamera
            {
                DeviceId = deviceId,
                IpAddress = ipAddress,
                Name = name,
                Model = model,
                CameraSeries = series,
                MacAddress = mac,
                XAddr = xaddr,
                Scopes = scopes
            };
        }
    }

    private static string? ExtractHost(string? xaddr)
    {
        if (string.IsNullOrWhiteSpace(xaddr))
        {
            return null;
        }

        return Uri.TryCreate(xaddr, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    private static string? GetScopeValue(string scopes, string key)
    {
        if (string.IsNullOrWhiteSpace(scopes))
        {
            return null;
        }

        var prefix = $"onvif://www.onvif.org/{key}/";
        foreach (var token in scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(token[prefix.Length..]);
            }
        }

        return null;
    }

    private static bool TryToUInt32(IPAddress address, out uint value)
    {
        value = 0;
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        value = ((uint)bytes[0] << 24)
            | ((uint)bytes[1] << 16)
            | ((uint)bytes[2] << 8)
            | bytes[3];
        return true;
    }

    private static IReadOnlyList<(uint Start, uint End)> GetLocalIpv4Ranges(int maxTargets)
    {
        var ranges = new List<(uint Start, uint End)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = nic.GetIPProperties();
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (!TryToUInt32(unicast.Address, out var ipValue))
                {
                    continue;
                }

                if (!IsPrivateIpv4(ipValue))
                {
                    continue;
                }

                var mask = unicast.IPv4Mask;
                if (mask is null || !TryToUInt32(mask, out var maskValue))
                {
                    continue;
                }

                var network = ipValue & maskValue;
                var broadcast = network | ~maskValue;
                if (broadcast <= network + 1)
                {
                    continue;
                }

                var start = network + 1;
                var end = broadcast - 1;
                var hostCount = end - start + 1;
                if (hostCount > (uint)maxTargets)
                {
                    var classCNetwork = ipValue & 0xFFFFFF00;
                    start = classCNetwork + 1;
                    end = classCNetwork + 254;
                    hostCount = end - start + 1;
                    if (hostCount > (uint)maxTargets)
                    {
                        continue;
                    }
                }

                var key = $"{start}-{end}";
                if (seen.Add(key))
                {
                    ranges.Add((start, end));
                }
            }
        }

        return ranges;
    }

    private static bool IsPrivateIpv4(uint ipValue)
    {
        var first = (byte)(ipValue >> 24);
        var second = (byte)(ipValue >> 16);
        return first == 10
            || (first == 172 && second >= 16 && second <= 31)
            || (first == 192 && second == 168);
    }

    private static IEnumerable<IPAddress> ExpandIpv4Range(uint startValue, uint endValue)
    {
        var current = startValue;
        while (true)
        {
            var bytes = new[]
            {
                (byte)(current >> 24),
                (byte)(current >> 16),
                (byte)(current >> 8),
                (byte)current
            };
            yield return new IPAddress(bytes);
            if (current == endValue)
            {
                yield break;
            }

            current++;
        }
    }

    private static bool IsIproCamera(DiscoveredCamera camera)
    {
        return LooksLikeIproValue(camera.Model) || LooksLikeIproValue(camera.Scopes);
    }

    private static bool LooksLikeIproValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("WV-", StringComparison.OrdinalIgnoreCase)
            || value.Contains("i-pro", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ipro", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveCameraSeries(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var upper = model.Trim().ToUpperInvariant();
        var index = upper.IndexOf("WV-", StringComparison.Ordinal);
        if (index >= 0 && index + 3 < upper.Length)
        {
            var series = upper[index + 3];
            return series switch
            {
                'X' => "X",
                'S' => "S",
                _ => null
            };
        }

        return null;
    }
}
