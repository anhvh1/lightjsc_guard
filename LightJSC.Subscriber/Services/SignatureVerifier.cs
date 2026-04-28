using System.Security.Cryptography;
using System.Text;
using LightJSC.Subscriber.Options;
using Microsoft.Extensions.Options;

namespace LightJSC.Subscriber.Services;

public sealed class SignatureVerifier
{
    private readonly SubscriberOptions _options;

    public SignatureVerifier(IOptions<SubscriberOptions> options)
    {
        _options = options.Value;
    }

    public bool IsValid(HttpContext context, string body)
    {
        if (string.IsNullOrWhiteSpace(_options.HmacSecret))
        {
            return !_options.RequireSignature || context.Request.Headers.ContainsKey("X-Signature");
        }

        var provided = context.Request.Headers["X-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(provided))
        {
            return !_options.RequireSignature;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var keyBytes = Encoding.UTF8.GetBytes(_options.HmacSecret);
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

