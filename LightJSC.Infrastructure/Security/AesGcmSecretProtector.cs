using System.Security.Cryptography;
using System.Text;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Options;
using Microsoft.Extensions.Options;

namespace LightJSC.Infrastructure.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(IOptions<EncryptionOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Base64Key))
        {
            throw new InvalidOperationException("Encryption key is missing. Set Encryption:Base64Key.");
        }

        _key = Convert.FromBase64String(options.Value.Base64Key);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Encryption key must be 32 bytes (base64-encoded). Add a 256-bit key.");
        }
    }

    public string EncryptToBase64(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(payload);
    }

    public string DecryptFromBase64(string base64Ciphertext)
    {
        var payload = Convert.FromBase64String(base64Ciphertext);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Ciphertext is too short.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var ciphertext = payload.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}

