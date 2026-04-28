namespace LightJSC.Core.Interfaces;

public interface IQrCodeDecoder
{
    string? Decode(byte[] imageBytes);
}
