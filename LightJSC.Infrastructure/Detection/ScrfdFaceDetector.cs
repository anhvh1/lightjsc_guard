using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LightJSC.Infrastructure.Detection;

#pragma warning disable CA1416
public sealed class ScrfdFaceDetector : IFaceDetectorService, IDisposable
{
    private readonly FaceDetectionOptions _options;
    private readonly ILogger<ScrfdFaceDetector> _logger;
    private readonly Lazy<InferenceSession> _session;
    private readonly object _inferenceLock = new();

    public ScrfdFaceDetector(IOptions<FaceDetectionOptions> options, ILogger<ScrfdFaceDetector> logger)
    {
        _options = options.Value;
        _logger = logger;
        _session = new Lazy<InferenceSession>(() =>
        {
            var sessionOptions = new SessionOptions();
            return new InferenceSession(_options.ModelPath, sessionOptions);
        });
    }

    public IReadOnlyList<FaceDetectionResult> DetectFaces(byte[] imageBytes)
    {
        if (!_options.Enabled)
        {
            return Array.Empty<FaceDetectionResult>();
        }

        if (imageBytes is null || imageBytes.Length == 0)
        {
            return Array.Empty<FaceDetectionResult>();
        }

        if (!File.Exists(_options.ModelPath))
        {
            _logger.LogWarning("SCRFD model not found at {Path}.", _options.ModelPath);
            return Array.Empty<FaceDetectionResult>();
        }

        using var inputImage = LoadBitmap(imageBytes);
        _logger.LogDebug(
            "SCRFD input image size {Width}x{Height}, InputSize={InputSize}, ScoreThreshold={ScoreThreshold}, MinFaceSize={MinFaceSize}.",
            inputImage.Width,
            inputImage.Height,
            _options.InputSize,
            _options.ScoreThreshold,
            _options.MinFaceSize);
        var (inputTensor, resizeInfo) = PrepareInputTensor(inputImage, _options);
        var session = _session.Value;
        var inputName = session.InputMetadata.Keys.First();

        List<FaceCandidate> candidates;
        lock (_inferenceLock)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };
            using var outputs = session.Run(inputs);
            candidates = DecodeOutputs(outputs, resizeInfo, _options);
        }

        if (candidates.Count == 0)
        {
            _logger.LogInformation(
                "SCRFD produced 0 candidates (pre-NMS). Check model outputs, ScoreThreshold, MinFaceSize.");
        }
        else
        {
            var best = candidates[0];
            _logger.LogDebug(
                "SCRFD pre-NMS candidates={Count}, topScore={Score:F3}, topBox=({X:F1},{Y:F1},{W:F1},{H:F1}).",
                candidates.Count,
                best.Score,
                best.Box.X,
                best.Box.Y,
                best.Box.Width,
                best.Box.Height);
        }

        var selected = ApplyNms(candidates, _options.NmsThreshold, _options.MaxFaces);
        if (selected.Count == 0)
        {
            var topScores = candidates
                .Take(5)
                .Select(c => c.Score.ToString("F3"))
                .ToArray();
            _logger.LogInformation(
                "SCRFD NMS returned 0 faces. Top scores (pre-NMS): {Scores}.",
                topScores.Length == 0 ? "none" : string.Join(", ", topScores));
            return Array.Empty<FaceDetectionResult>();
        }

        var results = new List<FaceDetectionResult>(selected.Count);
        foreach (var candidate in selected)
        {
            var jpeg = ExtractFaceJpeg(inputImage, candidate.Box, candidate.Landmarks, _options);
            var landmarks = candidate.Landmarks is null
                ? null
                : candidate.Landmarks.Select(point => new FaceLandmark(point.X, point.Y)).ToArray();
            results.Add(new FaceDetectionResult(candidate.Box, candidate.Score, landmarks, jpeg));
        }

        return results;
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value.Dispose();
        }
    }

    private static Bitmap LoadBitmap(byte[] imageBytes)
    {
        using var stream = new MemoryStream(imageBytes);
        using var temp = Image.FromStream(stream);
        return new Bitmap(temp);
    }

    private sealed record ResizeInfo(float Scale, int PadX, int PadY, int InputSize, int OriginalWidth, int OriginalHeight);

    private static (DenseTensor<float> tensor, ResizeInfo info) PrepareInputTensor(Bitmap image, FaceDetectionOptions options)
    {
        var inputSize = Math.Max(64, options.InputSize);
        var originalWidth = image.Width;
        var originalHeight = image.Height;

        var scale = Math.Min((float)inputSize / originalWidth, (float)inputSize / originalHeight);
        var resizedWidth = Math.Max(1, (int)Math.Round(originalWidth * scale));
        var resizedHeight = Math.Max(1, (int)Math.Round(originalHeight * scale));
        var padX = (inputSize - resizedWidth) / 2;
        var padY = (inputSize - resizedHeight) / 2;

        using var resized = new Bitmap(inputSize, inputSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, new Rectangle(padX, padY, resizedWidth, resizedHeight));
        }

        var tensor = new DenseTensor<float>(new[] { 1, 3, inputSize, inputSize });
        var mean = options.Mean.Length == 3 ? options.Mean : new[] { 127.5f, 127.5f, 127.5f };
        var std = options.Std.Length == 3 ? options.Std : new[] { 128f, 128f, 128f };

        var rect = new Rectangle(0, 0, inputSize, inputSize);
        var data = resized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var buffer = new byte[stride * inputSize];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            for (var y = 0; y < inputSize; y++)
            {
                var rowOffset = y * stride;
                for (var x = 0; x < inputSize; x++)
                {
                    var idx = rowOffset + x * 3;
                    var b = buffer[idx + 0];
                    var g = buffer[idx + 1];
                    var r = buffer[idx + 2];

                    if (options.SwapRB)
                    {
                        (r, b) = (b, r);
                    }

                    tensor[0, 0, y, x] = (r - mean[0]) / std[0];
                    tensor[0, 1, y, x] = (g - mean[1]) / std[1];
                    tensor[0, 2, y, x] = (b - mean[2]) / std[2];
                }
            }
        }
        finally
        {
            resized.UnlockBits(data);
        }

        var info = new ResizeInfo(scale, padX, padY, inputSize, originalWidth, originalHeight);
        return (tensor, info);
    }

    private static List<FaceCandidate> DecodeOutputs(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
        ResizeInfo resizeInfo,
        FaceDetectionOptions options)
    {
        var tensors = outputs.ToDictionary(o => o.Name, o => o.AsTensor<float>());

        var featureMaps = GroupFeatureMaps(tensors, resizeInfo.InputSize);
        var candidates = new List<FaceCandidate>();

        foreach (var map in featureMaps)
        {
            var stride = map.Stride;
            var scoreTensor = map.Score;
            var bboxTensor = map.Bbox;
            var kpsTensor = map.Keypoints;
            if (map.IsFlattened)
            {
                var anchors = map.Anchors;
                var h = map.Height;
                var w = map.Width;
                var total = scoreTensor.Dimensions[0];
                for (var idx = 0; idx < total; idx++)
                {
                    var score = ReadFlatValue(scoreTensor, idx, 0);
                    if (score < options.ScoreThreshold)
                    {
                        continue;
                    }

                    var anchorIndex = idx % anchors;
                    var posIndex = idx / anchors;
                    var y = posIndex / w;
                    var x = posIndex % w;

                    var cx = (x + 0.5f) * stride;
                    var cy = (y + 0.5f) * stride;
                    var left = ReadFlatValue(bboxTensor, idx, 0) * stride;
                    var top = ReadFlatValue(bboxTensor, idx, 1) * stride;
                    var right = ReadFlatValue(bboxTensor, idx, 2) * stride;
                    var bottom = ReadFlatValue(bboxTensor, idx, 3) * stride;

                    var x1 = cx - left;
                    var y1 = cy - top;
                    var x2 = cx + right;
                    var y2 = cy + bottom;

                    var mapped = MapToOriginal(x1, y1, x2, y2, resizeInfo);
                    if (mapped.Width < options.MinFaceSize || mapped.Height < options.MinFaceSize)
                    {
                        continue;
                    }

                    IReadOnlyList<FaceLandmark>? landmarks = null;
                    if (kpsTensor is not null)
                    {
                        landmarks = DecodeLandmarksFlat(kpsTensor, idx, cx, cy, stride, resizeInfo);
                    }

                    candidates.Add(new FaceCandidate(mapped, score, landmarks));
                }
            }
            else
            {
                var h = scoreTensor.Dimensions[2];
                var w = scoreTensor.Dimensions[3];
                var scoreChannels = scoreTensor.Dimensions[1];
                var bboxChannels = bboxTensor.Dimensions[1];
                var numAnchors = Math.Max(1, bboxChannels / 4);

                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        for (var a = 0; a < numAnchors; a++)
                        {
                            var score = scoreChannels == 1
                                ? scoreTensor[0, 0, y, x]
                                : scoreTensor[0, a, y, x];

                            if (score < options.ScoreThreshold)
                            {
                                continue;
                            }

                            var cx = (x + 0.5f) * stride;
                            var cy = (y + 0.5f) * stride;
                            var baseIndex = a * 4;
                            var left = bboxTensor[0, baseIndex + 0, y, x] * stride;
                            var top = bboxTensor[0, baseIndex + 1, y, x] * stride;
                            var right = bboxTensor[0, baseIndex + 2, y, x] * stride;
                            var bottom = bboxTensor[0, baseIndex + 3, y, x] * stride;

                            var x1 = cx - left;
                            var y1 = cy - top;
                            var x2 = cx + right;
                            var y2 = cy + bottom;

                            var mapped = MapToOriginal(x1, y1, x2, y2, resizeInfo);
                            if (mapped.Width < options.MinFaceSize || mapped.Height < options.MinFaceSize)
                            {
                                continue;
                            }

                            var landmarks = kpsTensor is null
                                ? null
                                : DecodeLandmarks(kpsTensor, a, x, y, cx, cy, stride, resizeInfo);

                            candidates.Add(new FaceCandidate(mapped, score, landmarks));
                        }
                    }
                }
            }
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        return candidates;
    }

    private static IReadOnlyList<FeatureMap> GroupFeatureMaps(Dictionary<string, Tensor<float>> tensors, int inputSize)
    {
        var maps = new List<FeatureMap>();
        foreach (var group in tensors.Values
                     .Where(t => t.Dimensions.Length == 4)
                     .GroupBy(t => (h: t.Dimensions[2], w: t.Dimensions[3])))
        {
            var score = group.FirstOrDefault(t => t.Dimensions[1] <= 2);
            var bbox = group.FirstOrDefault(t => t.Dimensions[1] % 4 == 0 && t.Dimensions[1] >= 4);
            if (score is null || bbox is null)
            {
                continue;
            }

            var kps = group.FirstOrDefault(t => t.Dimensions[1] % 10 == 0 && t.Dimensions[1] >= 10);
            var stride = score.Dimensions[2] > 0 ? inputSize / score.Dimensions[2] : 1;
            maps.Add(new FeatureMap(score, bbox, kps) { Stride = stride });
        }

        if (maps.Count == 0)
        {
            maps.AddRange(GroupFlattenedFeatureMaps(tensors, inputSize));
        }

        if (maps.Count == 0)
        {
            throw new InvalidOperationException("SCRFD outputs not recognized.");
        }

        return maps;
    }

    private static IReadOnlyList<FeatureMap> GroupFlattenedFeatureMaps(
        Dictionary<string, Tensor<float>> tensors,
        int inputSize)
    {
        var maps = new List<FeatureMap>();
        var scoreGroups = tensors.Values
            .Where(t => IsFlattenedTensor(t, 1))
            .ToDictionary(GetFlattenedCount, t => t);
        var bboxGroups = tensors.Values
            .Where(t => IsFlattenedTensor(t, 4))
            .ToDictionary(GetFlattenedCount, t => t);
        var kpsGroups = tensors.Values
            .Where(t => IsFlattenedTensor(t, 10))
            .ToDictionary(GetFlattenedCount, t => t);

        foreach (var (count, scoreTensor) in scoreGroups)
        {
            if (!bboxGroups.TryGetValue(count, out var bboxTensor))
            {
                continue;
            }

            kpsGroups.TryGetValue(count, out var kpsTensor);

            if (!TryInferStride(inputSize, count, out var stride, out var anchors, out var height, out var width))
            {
                continue;
            }

            var map = new FeatureMap(scoreTensor, bboxTensor, kpsTensor)
            {
                Stride = stride,
                IsFlattened = true,
                Height = height,
                Width = width,
                Anchors = anchors
            };
            maps.Add(map);
        }

        return maps;
    }

    private static bool IsFlattenedTensor(Tensor<float> tensor, int channels)
    {
        return tensor.Dimensions.Length switch
        {
            2 => tensor.Dimensions[1] == channels,
            3 => tensor.Dimensions[0] == 1 && tensor.Dimensions[2] == channels,
            _ => false
        };
    }

    private static int GetFlattenedCount(Tensor<float> tensor)
    {
        return tensor.Dimensions.Length switch
        {
            2 => tensor.Dimensions[0],
            3 => tensor.Dimensions[1],
            _ => 0
        };
    }

    private static bool TryInferStride(
        int inputSize,
        int flattenedCount,
        out int stride,
        out int anchors,
        out int height,
        out int width)
    {
        stride = 0;
        anchors = 0;
        height = 0;
        width = 0;

        var candidateStrides = new[] { 8, 16, 32, 64 };
        foreach (var candidate in candidateStrides)
        {
            if (inputSize % candidate != 0)
            {
                continue;
            }

            var size = inputSize / candidate;
            var positions = size * size;
            if (positions == 0 || flattenedCount % positions != 0)
            {
                continue;
            }

            var anchorCount = flattenedCount / positions;
            if (anchorCount < 1 || anchorCount > 4)
            {
                continue;
            }

            stride = candidate;
            anchors = anchorCount;
            height = size;
            width = size;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<FaceLandmark>? DecodeLandmarks(
        Tensor<float> kpsTensor,
        int anchorIndex,
        int x,
        int y,
        float cx,
        float cy,
        int stride,
        ResizeInfo resizeInfo)
    {
        var channels = kpsTensor.Dimensions[1];
        var expectedAnchors = Math.Max(1, channels / 10);
        if (anchorIndex >= expectedAnchors)
        {
            return null;
        }

        var landmarks = new List<FaceLandmark>(5);
        var baseIndex = anchorIndex * 10;
        for (var i = 0; i < 5; i++)
        {
            var dx = kpsTensor[0, baseIndex + i * 2 + 0, y, x] * stride;
            var dy = kpsTensor[0, baseIndex + i * 2 + 1, y, x] * stride;
            var lx = cx + dx;
            var ly = cy + dy;
            var mapped = MapPointToOriginal(lx, ly, resizeInfo);
            landmarks.Add(new FaceLandmark(mapped.X, mapped.Y));
        }

        return landmarks;
    }

    private static IReadOnlyList<FaceLandmark> DecodeLandmarksFlat(
        Tensor<float> kpsTensor,
        int index,
        float cx,
        float cy,
        int stride,
        ResizeInfo resizeInfo)
    {
        var landmarks = new List<FaceLandmark>(5);
        for (var i = 0; i < 5; i++)
        {
            var dx = ReadFlatValue(kpsTensor, index, i * 2) * stride;
            var dy = ReadFlatValue(kpsTensor, index, i * 2 + 1) * stride;
            var lx = cx + dx;
            var ly = cy + dy;
            var mapped = MapPointToOriginal(lx, ly, resizeInfo);
            landmarks.Add(new FaceLandmark(mapped.X, mapped.Y));
        }

        return landmarks;
    }

    private static float ReadFlatValue(Tensor<float> tensor, int index, int channel)
    {
        return tensor.Dimensions.Length switch
        {
            2 => tensor[index, channel],
            3 => tensor[0, index, channel],
            _ => throw new InvalidOperationException("Unexpected SCRFD tensor shape.")
        };
    }

    private static FaceBox MapToOriginal(float x1, float y1, float x2, float y2, ResizeInfo resizeInfo)
    {
        var scale = resizeInfo.Scale;
        var padX = resizeInfo.PadX;
        var padY = resizeInfo.PadY;
        var ox1 = (x1 - padX) / scale;
        var oy1 = (y1 - padY) / scale;
        var ox2 = (x2 - padX) / scale;
        var oy2 = (y2 - padY) / scale;

        ox1 = Math.Clamp(ox1, 0, resizeInfo.OriginalWidth - 1);
        oy1 = Math.Clamp(oy1, 0, resizeInfo.OriginalHeight - 1);
        ox2 = Math.Clamp(ox2, 0, resizeInfo.OriginalWidth - 1);
        oy2 = Math.Clamp(oy2, 0, resizeInfo.OriginalHeight - 1);

        return new FaceBox(ox1, oy1, Math.Max(1, ox2 - ox1), Math.Max(1, oy2 - oy1));
    }

    private static FaceLandmark MapPointToOriginal(float x, float y, ResizeInfo resizeInfo)
    {
        var scale = resizeInfo.Scale;
        var padX = resizeInfo.PadX;
        var padY = resizeInfo.PadY;
        var ox = (x - padX) / scale;
        var oy = (y - padY) / scale;
        ox = Math.Clamp(ox, 0, resizeInfo.OriginalWidth - 1);
        oy = Math.Clamp(oy, 0, resizeInfo.OriginalHeight - 1);
        return new FaceLandmark(ox, oy);
    }

    private static List<FaceCandidate> ApplyNms(List<FaceCandidate> candidates, float threshold, int maxFaces)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var selected = new List<FaceCandidate>();
        foreach (var candidate in candidates)
        {
            var shouldKeep = true;
            foreach (var picked in selected)
            {
                if (IoU(candidate.Box, picked.Box) > threshold)
                {
                    shouldKeep = false;
                    break;
                }
            }

            if (!shouldKeep)
            {
                continue;
            }

            selected.Add(candidate);
            if (selected.Count >= maxFaces)
            {
                break;
            }
        }

        return selected;
    }

    private static float IoU(FaceBox a, FaceBox b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        var interW = Math.Max(0, x2 - x1);
        var interH = Math.Max(0, y2 - y1);
        var inter = interW * interH;
        var areaA = a.Width * a.Height;
        var areaB = b.Width * b.Height;
        return inter / (areaA + areaB - inter + 1e-5f);
    }

    private static byte[] ExtractFaceJpeg(
        Bitmap source,
        FaceBox box,
        IReadOnlyList<FaceLandmark>? landmarks,
        FaceDetectionOptions options)
    {
        var outputSize = Math.Max(32, options.OutputSize);
        if (options.AlignEnabled && landmarks is { Count: >= 5 })
        {
            var aligned = AlignFace(source, landmarks, outputSize, options.AlignScale);
            if (aligned is not null)
            {
                return EncodeJpeg(aligned, options.JpegQuality);
            }
        }

        var margin = Math.Max(0f, options.MarginRatio);
        var centerX = box.X + box.Width / 2f;
        var centerY = box.Y + box.Height / 2f;
        var size = Math.Max(box.Width, box.Height) * (1f + margin * 2f);
        var half = size / 2f;
        var srcRect = new RectangleF(centerX - half, centerY - half, size, size);

        using var dest = new Bitmap(outputSize, outputSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(dest))
        {
            graphics.Clear(Color.FromArgb(128, 128, 128));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;

            var srcImageRect = new RectangleF(0, 0, source.Width, source.Height);
            var intersect = RectangleF.Intersect(srcRect, srcImageRect);
            if (intersect.Width > 1 && intersect.Height > 1)
            {
                var destX = (intersect.X - srcRect.X) / srcRect.Width * outputSize;
                var destY = (intersect.Y - srcRect.Y) / srcRect.Height * outputSize;
                var destW = intersect.Width / srcRect.Width * outputSize;
                var destH = intersect.Height / srcRect.Height * outputSize;
                graphics.DrawImage(
                    source,
                    new RectangleF(destX, destY, destW, destH),
                    intersect,
                    GraphicsUnit.Pixel);
            }
        }

        return EncodeJpeg(dest, options.JpegQuality);
    }

    private static Bitmap? AlignFace(
        Bitmap source,
        IReadOnlyList<FaceLandmark> landmarks,
        int outputSize,
        float alignScale)
    {
        if (landmarks.Count < 5)
        {
            return null;
        }

        var template = GetTemplatePoints(outputSize, alignScale);
        var src = landmarks.Take(5).Select(p => new PointF(p.X, p.Y)).ToArray();
        var dst = template;

        if (!TryEstimateAffine(src, dst, out var a, out var b, out var c, out var d, out var e, out var f))
        {
            return null;
        }

        var dest = new Bitmap(outputSize, outputSize, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(dest);
        graphics.Clear(Color.FromArgb(128, 128, 128));
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;

        using var matrix = new Matrix((float)a, (float)d, (float)b, (float)e, (float)c, (float)f);
        graphics.Transform = matrix;
        graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
        graphics.ResetTransform();
        return dest;
    }

    private static PointF[] GetTemplatePoints(int outputSize, float alignScale)
    {
        // ArcFace canonical 112x112 template scaled to outputSize
        var baseScale = outputSize / 112f;
        var scale = baseScale;
        var points = new[]
        {
            new PointF(38.2946f * scale, 51.6963f * scale),
            new PointF(73.5318f * scale, 51.5014f * scale),
            new PointF(56.0252f * scale, 71.7366f * scale),
            new PointF(41.5493f * scale, 92.3655f * scale),
            new PointF(70.7299f * scale, 92.2041f * scale)
        };

        if (alignScale <= 0f || Math.Abs(alignScale - 1f) < 0.001f)
        {
            return points;
        }

        // Shrink/expand around center to control padding.
        var center = new PointF(outputSize / 2f, outputSize / 2f);
        for (var i = 0; i < points.Length; i++)
        {
            var dx = points[i].X - center.X;
            var dy = points[i].Y - center.Y;
            points[i] = new PointF(
                center.X + dx * alignScale,
                center.Y + dy * alignScale);
        }

        return points;
    }

    private static bool TryEstimateAffine(
        PointF[] src,
        PointF[] dst,
        out double a,
        out double b,
        out double c,
        out double d,
        out double e,
        out double f)
    {
        a = b = c = d = e = f = 0d;
        if (src.Length != dst.Length || src.Length < 3)
        {
            return false;
        }

        var n = src.Length;
        var ata = new double[6, 6];
        var atb = new double[6];

        for (var i = 0; i < n; i++)
        {
            var x = src[i].X;
            var y = src[i].Y;
            var u = dst[i].X;
            var v = dst[i].Y;

            var row1 = new[] { x, y, 1d, 0d, 0d, 0d };
            var row2 = new[] { 0d, 0d, 0d, x, y, 1d };

            Accumulate(ata, atb, row1, u);
            Accumulate(ata, atb, row2, v);
        }

        var solution = SolveLinearSystem(ata, atb);
        if (solution is null)
        {
            return false;
        }

        a = solution[0];
        b = solution[1];
        c = solution[2];
        d = solution[3];
        e = solution[4];
        f = solution[5];
        return true;
    }

    private static void Accumulate(double[,] ata, double[] atb, double[] row, double value)
    {
        for (var i = 0; i < 6; i++)
        {
            atb[i] += row[i] * value;
            for (var j = 0; j < 6; j++)
            {
                ata[i, j] += row[i] * row[j];
            }
        }
    }

    private static double[]? SolveLinearSystem(double[,] a, double[] b)
    {
        var n = b.Length;
        var aug = new double[n, n + 1];

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                aug[i, j] = a[i, j];
            }
            aug[i, n] = b[i];
        }

        for (var i = 0; i < n; i++)
        {
            var maxRow = i;
            var maxVal = Math.Abs(aug[i, i]);
            for (var k = i + 1; k < n; k++)
            {
                var val = Math.Abs(aug[k, i]);
                if (val > maxVal)
                {
                    maxVal = val;
                    maxRow = k;
                }
            }

            if (maxVal < 1e-8)
            {
                return null;
            }

            if (maxRow != i)
            {
                for (var j = i; j <= n; j++)
                {
                    (aug[i, j], aug[maxRow, j]) = (aug[maxRow, j], aug[i, j]);
                }
            }

            var pivot = aug[i, i];
            for (var j = i; j <= n; j++)
            {
                aug[i, j] /= pivot;
            }

            for (var k = 0; k < n; k++)
            {
                if (k == i)
                {
                    continue;
                }

                var factor = aug[k, i];
                for (var j = i; j <= n; j++)
                {
                    aug[k, j] -= factor * aug[i, j];
                }
            }
        }

        var solution = new double[n];
        for (var i = 0; i < n; i++)
        {
            solution[i] = aug[i, n];
        }

        return solution;
    }

    private static byte[] EncodeJpeg(Bitmap image, int quality)
    {
        using var output = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        if (encoder is null)
        {
            image.Save(output, ImageFormat.Jpeg);
            return output.ToArray();
        }

        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        image.Save(output, encoder, encoderParams);
        return output.ToArray();
    }

    private sealed record FaceCandidate(FaceBox Box, float Score, IReadOnlyList<FaceLandmark>? Landmarks);

    private sealed record FeatureMap(Tensor<float> Score, Tensor<float> Bbox, Tensor<float>? Keypoints)
    {
        public int Stride { get; set; }
        public bool IsFlattened { get; init; }
        public int Height { get; init; }
        public int Width { get; init; }
        public int Anchors { get; init; }
    }
}
#pragma warning restore CA1416
