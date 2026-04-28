namespace LightJSC.Core.Models;

public readonly record struct FaceBox(float X, float Y, float Width, float Height);

public readonly record struct FaceLandmark(float X, float Y);

public sealed class FaceDetectionResult
{
    public FaceDetectionResult(FaceBox box, float score, IReadOnlyList<FaceLandmark>? landmarks, byte[] faceJpeg)
    {
        Box = box;
        Score = score;
        Landmarks = landmarks;
        FaceJpeg = faceJpeg;
    }

    public FaceBox Box { get; }
    public float Score { get; }
    public IReadOnlyList<FaceLandmark>? Landmarks { get; }
    public byte[] FaceJpeg { get; }
}
