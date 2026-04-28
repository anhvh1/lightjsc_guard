using System;
using System.IO;

namespace LightJSC.Infrastructure.Enrollment;

public static class LightGuardAppData
{
    private const int HeaderLength = 24;

    // Build s_appData = Base64( header(24 bytes) + jpegBytes )
    // header = 6 x uint32 big-endian: 0,0,0,0, 1, jpegLen
    public static string BuildSAppDataFromJpegBytes(byte[] jpegBytes, uint typeOrFlag = 1)
    {
        if (jpegBytes is null || jpegBytes.Length < 4)
        {
            throw new ArgumentException("JPEG bytes are empty.");
        }

        if (!(jpegBytes[0] == 0xFF && jpegBytes[1] == 0xD8))
        {
            throw new ArgumentException("Not a JPEG: missing SOI (FFD8).");
        }

        if (!(jpegBytes[^2] == 0xFF && jpegBytes[^1] == 0xD9))
        {
            throw new ArgumentException("Not a JPEG: missing EOI (FFD9).");
        }

        var payloadLen = checked((uint)jpegBytes.Length);
        using var ms = new MemoryStream(capacity: HeaderLength + jpegBytes.Length);

        WriteU32BE(ms, 0);
        WriteU32BE(ms, 0);
        WriteU32BE(ms, 0);
        WriteU32BE(ms, 0);
        WriteU32BE(ms, typeOrFlag);
        WriteU32BE(ms, payloadLen);
        ms.Write(jpegBytes, 0, jpegBytes.Length);

        return Convert.ToBase64String(ms.ToArray(), Base64FormattingOptions.None);
    }

    public static string BuildSAppDataFromJpegFile(string jpegPath, uint typeOrFlag = 1)
    {
        if (string.IsNullOrWhiteSpace(jpegPath))
        {
            throw new ArgumentException("jpegPath is empty.");
        }

        var jpegBytes = File.ReadAllBytes(jpegPath);
        return BuildSAppDataFromJpegBytes(jpegBytes, typeOrFlag);
    }

    private static void WriteU32BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value));
    }
}
