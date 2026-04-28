using System.Security.Cryptography;
using System.Text;
using LightJSC.Core.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Subscriber;

public sealed class SignatureVerifier
{
    private readonly IOptionsMonitor<SubscriberServiceOptions> _options;
    private readonly IOptionsMonitor<WebhookOptions> _webhookOptions;

    public SignatureVerifier(IOptionsMonitor<SubscriberServiceOptions> options, IOptionsMonitor<WebhookOptions> webhookOptions)
    {
        _options = options;
        _webhookOptions = webhookOptions;
    }

    public bool IsValid(HttpContext context, string body)
    {
        var config = _options.CurrentValue;
        var secret = string.IsNullOrWhiteSpace(config.HmacSecret) ? _webhookOptions.CurrentValue.HmacSecret : config.HmacSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            return !config.RequireSignature || context.Request.Headers.ContainsKey("X-Signature");
        }

        var provided = context.Request.Headers["X-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            return !config.RequireSignature;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(keyBytes);
        var computed = hmac.ComputeHash(bodyBytes);

        try
        {
            var providedBytes = Convert.FromHexString(provided.Trim());
            return CryptographicOperations.FixedTimeEquals(computed, providedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
