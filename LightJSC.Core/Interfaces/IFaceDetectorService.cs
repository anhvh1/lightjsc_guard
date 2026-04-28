using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IFaceDetectorService
{
    IReadOnlyList<FaceDetectionResult> DetectFaces(byte[] imageBytes);
}
