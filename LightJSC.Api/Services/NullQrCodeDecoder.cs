using LightJSC.Core.Interfaces;

namespace LightJSC.Api.Services;

public sealed class NullQrCodeDecoder : IQrCodeDecoder
{
    public string? Decode(byte[] imageBytes)
    {
        _ = imageBytes;
        return null;
    }
}
