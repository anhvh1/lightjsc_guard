namespace LightJSC.Core.Options;

public sealed class FaceDetectionOptions
{
    public bool Enabled { get; set; } = true;
    public string ModelPath { get; set; } = "models/scrfd.onnx";
    public int InputSize { get; set; } = 640;
    public float ScoreThreshold { get; set; } = 0.5f;
    public float NmsThreshold { get; set; } = 0.4f;
    public int MaxFaces { get; set; } = 10;
    public int MinFaceSize { get; set; } = 48;
    public float MarginRatio { get; set; } = 0.25f;
    public int OutputSize { get; set; } = 100;
    public int JpegQuality { get; set; } = 90;
    public bool AlignEnabled { get; set; } = true;
    public float AlignScale { get; set; } = 0.9f;
    public bool SwapRB { get; set; } = true;
    public float[] Mean { get; set; } = new[] { 127.5f, 127.5f, 127.5f };
    public float[] Std { get; set; } = new[] { 128f, 128f, 128f };
}
