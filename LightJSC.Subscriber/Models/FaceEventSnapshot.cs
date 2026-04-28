namespace LightJSC.Subscriber.Models;

public sealed class FaceEventSnapshot
{
    public IReadOnlyList<FaceEventDto> Known { get; init; } = Array.Empty<FaceEventDto>();
    public IReadOnlyList<FaceEventDto> Unknown { get; init; } = Array.Empty<FaceEventDto>();
    public long KnownTotal { get; init; }
    public long UnknownTotal { get; init; }
}

