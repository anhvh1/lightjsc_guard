using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IFaceMetadataParser
{
    bool TryParse(CameraMetadata camera, string payload, DateTimeOffset receivedAtUtc, out FaceEvent faceEvent);
}

