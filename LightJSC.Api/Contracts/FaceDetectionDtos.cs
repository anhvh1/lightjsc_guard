namespace LightJSC.Api.Contracts;

public sealed class FaceDetectRequest
{
    public string ImageBase64 { get; set; } = string.Empty;
    public int? MaxFaces { get; set; }
}

public sealed class FaceDetectResponse
{
    public string FaceId { get; set; } = string.Empty;
    public float Score { get; set; }
    public FaceBoxResponse Box { get; set; } = new();
    public string ThumbnailBase64 { get; set; } = string.Empty;
}

public sealed class FaceBoxResponse
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}
