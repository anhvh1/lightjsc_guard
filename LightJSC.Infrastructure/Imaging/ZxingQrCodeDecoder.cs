using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using LightJSC.Core.Interfaces;
using Microsoft.Extensions.Logging;
using ZXing;
using ZXing.Common;

namespace LightJSC.Infrastructure.Imaging;

[SupportedOSPlatform("windows")]
public sealed class ZxingQrCodeDecoder : IQrCodeDecoder
{
    private readonly ILogger<ZxingQrCodeDecoder> _logger;

    public ZxingQrCodeDecoder(ILogger<ZxingQrCodeDecoder> logger)
    {
        _logger = logger;
    }

    public string? Decode(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        try
        {
            using var stream = new MemoryStream(imageBytes);
            using var original = new Bitmap(stream);
            using var bitmap = EnsureBgra32(original);
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                var length = Math.Abs(data.Stride) * data.Height;
                var pixels = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, length);

                var source = new RGBLuminanceSource(
                    pixels,
                    data.Width,
                    data.Height,
                    RGBLuminanceSource.BitmapFormat.BGRA32);

                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };

                return reader.Decode(source)?.Text;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QR decode failed.");
            return null;
        }
    }

    private static Bitmap EnsureBgra32(Bitmap source)
    {
        if (source.PixelFormat == PixelFormat.Format32bppArgb)
        {
            return new Bitmap(source);
        }

        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        return clone;
    }
}
