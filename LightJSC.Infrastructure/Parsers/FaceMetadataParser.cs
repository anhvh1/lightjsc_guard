using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using LightJSC.Core.Options;
using LightJSC.Infrastructure.Vector;
using Microsoft.Extensions.Options;

namespace LightJSC.Infrastructure.Parsers;

public sealed class FaceMetadataParser : IFaceMetadataParser
{
    private readonly int _maxEventTimeSkewMinutes;

    public FaceMetadataParser()
        : this(Options.Create(new IngestOptions()))
    {
    }

    public FaceMetadataParser(IOptions<IngestOptions> options)
    {
        _maxEventTimeSkewMinutes = Math.Max(0, options.Value.MaxEventTimeSkewMinutes);
    }

    public bool TryParse(CameraMetadata camera, string payload, DateTimeOffset receivedAtUtc, out FaceEvent faceEvent)
    {
        faceEvent = new FaceEvent();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var trimmed = payload.AsSpan().TrimStart();
        if (trimmed.StartsWith("<".AsSpan(), StringComparison.Ordinal))
        {
            if (TryParseXml(camera, payload, receivedAtUtc, _maxEventTimeSkewMinutes, out faceEvent))
            {
                return true;
            }
        }

        if (trimmed.StartsWith("{".AsSpan(), StringComparison.Ordinal) || trimmed.StartsWith("[".AsSpan(), StringComparison.Ordinal))
        {
            if (TryParseJson(camera, payload, receivedAtUtc, _maxEventTimeSkewMinutes, out faceEvent))
            {
                return true;
            }
        }

        return TryParseKeyValue(camera, payload, receivedAtUtc, _maxEventTimeSkewMinutes, out faceEvent);
    }

    private static bool TryParseJson(
        CameraMetadata camera,
        string payload,
        DateTimeOffset receivedAtUtc,
        int maxEventTimeSkewMinutes,
        out FaceEvent faceEvent)
    {
        faceEvent = new FaceEvent();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (TryParseJsonElement(camera, element, receivedAtUtc, maxEventTimeSkewMinutes, out faceEvent))
                    {
                        return true;
                    }
                }

                return false;
            }

            return TryParseJsonElement(camera, root, receivedAtUtc, maxEventTimeSkewMinutes, out faceEvent);
        }
        catch
        {
            faceEvent = new FaceEvent();
            return false;
        }
    }

    private static bool TryParseJsonElement(
        CameraMetadata camera,
        System.Text.Json.JsonElement element,
        DateTimeOffset receivedAtUtc,
        int maxEventTimeSkewMinutes,
        out FaceEvent faceEvent)
    {
        faceEvent = new FaceEvent();
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return false;
        }

        string? featureValue = GetJsonString(element, "FeatureValue", "featureValue", "feature");
        string? featureVersion = GetJsonString(element, "feature-value-version", "featureValueVersion", "featurevalueversion");
        string? l2NormText = GetJsonString(element, "L2Norm", "l2Norm", "l2norm");
        string? startTime = GetJsonString(element, "start-time", "startTime", "StartTime", "UtcTime", "utcTime");
        string? ageText = GetJsonString(element, "Age", "age");
        string? gender = GetJsonString(element, "Gender", "gender");
        string? mask = GetJsonString(element, "Mask", "mask");
        string? faceImageBase64 = GetJsonString(element, "Image", "image", "FaceImage", "faceImage");
        string? bsFrame = GetJsonString(element, "bs-frame", "bsFrame");
        string? thumbFrame = GetJsonString(element, "thumb-frame", "thumbFrame");
        string? scoreText = GetJsonString(element, "Score", "score", "bs-score", "bsScore");

        BoundingBox? bbox = null;
        if (element.TryGetProperty("BBox", out var bboxObj)
            || element.TryGetProperty("bbox", out bboxObj)
            || element.TryGetProperty("BoundingBox", out bboxObj)
            || element.TryGetProperty("boundingBox", out bboxObj))
        {
            if (bboxObj.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var x = GetJsonFloat(bboxObj, "x", "left", "X");
                var y = GetJsonFloat(bboxObj, "y", "top", "Y");
                var w = GetJsonFloat(bboxObj, "width", "w", "Width");
                var h = GetJsonFloat(bboxObj, "height", "h", "Height");
                if (x.HasValue && y.HasValue && w.HasValue && h.HasValue)
                {
                    bbox = new BoundingBox(x.Value, y.Value, w.Value, h.Value);
                }
            }
        }

        return TryBuildFaceEvent(
            camera,
            receivedAtUtc,
            maxEventTimeSkewMinutes,
            featureValue,
            featureVersion,
            l2NormText,
            startTime,
            ageText,
            gender,
            mask,
            faceImageBase64,
            bsFrame,
            thumbFrame,
            scoreText,
            bbox,
            out faceEvent);
    }

    private static string? GetJsonString(System.Text.Json.JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var value))
            {
                if (value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind is System.Text.Json.JsonValueKind.Number
                    or System.Text.Json.JsonValueKind.True
                    or System.Text.Json.JsonValueKind.False)
                {
                    return value.ToString();
                }
            }
        }

        return null;
    }

    private static float? GetJsonFloat(System.Text.Json.JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var value))
            {
                if (value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetSingle(out var result))
                {
                    return result;
                }

                if (value.ValueKind == System.Text.Json.JsonValueKind.String
                    && float.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static bool TryParseXml(
        CameraMetadata camera,
        string payload,
        DateTimeOffset receivedAtUtc,
        int maxEventTimeSkewMinutes,
        out FaceEvent faceEvent)
    {
        string? featureValue = null;
        string? featureVersion = null;
        string? l2NormText = null;
        string? startTime = null;
        string? ageText = null;
        string? gender = null;
        string? mask = null;
        string? faceImageBase64 = null;
        string? bsFrame = null;
        string? thumbFrame = null;
        string? scoreText = null;
        BoundingBox? bbox = null;
        var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var reader = XmlReader.Create(new StringReader(payload), new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true
            });

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var localName = reader.LocalName;
                    var lowerName = localName.ToLowerInvariant();

                    if (reader.HasAttributes)
                    {
                        var utcTime = reader.GetAttribute("UtcTime");
                        if (!string.IsNullOrWhiteSpace(utcTime))
                        {
                            startTime ??= utcTime;
                        }

                        if (lowerName == "boundingbox" || lowerName == "bbox")
                        {
                            if (TryParseBoundingBox(reader, out var parsedBox))
                            {
                                bbox = parsedBox;
                            }
                        }
                    }

                    if (lowerName == "simpleitem")
                    {
                        var name = reader.GetAttribute("Name") ?? reader.GetAttribute("name");
                        var value = reader.GetAttribute("Value") ?? reader.GetAttribute("value");
                        SetItem(items, name, value);
                        continue;
                    }

                    if (lowerName == "elementitem")
                    {
                        var name = reader.GetAttribute("Name") ?? reader.GetAttribute("name");
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            var element = XElement.ReadFrom(reader) as XElement;
                            var value = element?.Value;
                            SetItem(items, name, value);
                            continue;
                        }
                    }

                    switch (lowerName)
                    {
                        case "image":
                            if (faceImageBase64 is null)
                            {
                                var image = reader.ReadElementContentAsString();
                                if (!string.IsNullOrWhiteSpace(image))
                                {
                                    faceImageBase64 = image.Trim();
                                }
                            }
                            else
                            {
                                reader.Skip();
                            }
                            break;
                        case "featurevalue":
                            featureValue = reader.ReadElementContentAsString();
                            break;
                        case "feature-value-version":
                        case "featurevalueversion":
                            featureVersion = reader.ReadElementContentAsString();
                            break;
                        case "l2norm":
                            l2NormText = reader.ReadElementContentAsString();
                            break;
                        case "start-time":
                        case "starttime":
                            startTime = reader.ReadElementContentAsString();
                            break;
                        case "age":
                            ageText = ParseAgeText(reader.ReadOuterXml());
                            break;
                        case "gender":
                            gender = ParseGenderText(reader.ReadOuterXml());
                            break;
                        case "mask":
                            mask = reader.ReadElementContentAsString();
                            break;
                        case "bs-frame":
                        case "bsframe":
                            bsFrame = reader.ReadElementContentAsString();
                            break;
                        case "thumb-frame":
                        case "thumbframe":
                            thumbFrame = reader.ReadElementContentAsString();
                            break;
                        case "score":
                            scoreText = reader.ReadElementContentAsString();
                            break;
                        case "bs-score":
                        case "bsscore":
                            scoreText ??= reader.ReadElementContentAsString();
                            break;
                    }
                }
            }
        }
        catch
        {
            faceEvent = new FaceEvent();
            return false;
        }

        featureValue ??= GetItem(items, "featurevalue", "feature");
        featureVersion ??= GetItem(items, "featurevalueversion", "featurevaluever");
        l2NormText ??= GetItem(items, "l2norm");
        startTime ??= GetItem(items, "starttime");
        ageText ??= GetItem(items, "age");
        gender ??= GetItem(items, "gender");
        mask ??= GetItem(items, "mask");
        bsFrame ??= GetItem(items, "bsframe");
        thumbFrame ??= GetItem(items, "thumbframe");
        scoreText ??= GetItem(items, "score");
        if (bbox is null)
        {
            var bboxText = GetItem(items, "bbox", "boundingbox");
            if (!string.IsNullOrWhiteSpace(bboxText))
            {
                bbox = ParseBoundingBoxValue(bboxText);
            }
        }

        ageText ??= ExtractSimpleElementValue(payload, "Age");
        gender ??= ExtractSimpleElementValue(payload, "Gender");
        mask ??= ExtractSimpleElementValue(payload, "Mask");
        bsFrame ??= ExtractSimpleElementValue(payload, "bs-frame") ?? ExtractSimpleElementValue(payload, "bsframe");
        thumbFrame ??= ExtractSimpleElementValue(payload, "thumb-frame") ?? ExtractSimpleElementValue(payload, "thumbframe");
        featureVersion ??= ExtractSimpleElementValue(payload, "feature-value-version")
            ?? ExtractSimpleElementValue(payload, "featurevalueversion");

        if (!string.IsNullOrWhiteSpace(ageText))
        {
            ageText = ParseAgeText(ageText) ?? ageText;
        }

        if (!string.IsNullOrWhiteSpace(gender))
        {
            gender = ParseGenderText(gender) ?? gender;
        }

        return TryBuildFaceEvent(
            camera,
            receivedAtUtc,
            maxEventTimeSkewMinutes,
            featureValue,
            featureVersion,
            l2NormText,
            startTime,
            ageText,
            gender,
            mask,
            faceImageBase64,
            bsFrame,
            thumbFrame,
            scoreText,
            bbox,
            out faceEvent);
    }

    private static bool TryParseKeyValue(
        CameraMetadata camera,
        string payload,
        DateTimeOffset receivedAtUtc,
        int maxEventTimeSkewMinutes,
        out FaceEvent faceEvent)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var segment in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = segment.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                var key = segment.Substring(0, index).Trim();
                var value = segment.Substring(index + 1).Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    map[key] = value;
                }
            }
        }

        map.TryGetValue("FeatureValue", out var featureValue);
        map.TryGetValue("feature-value-version", out var featureVersion);
        map.TryGetValue("L2Norm", out var l2Norm);
        map.TryGetValue("start-time", out var startTime);
        map.TryGetValue("Age", out var ageText);
        map.TryGetValue("Gender", out var gender);
        map.TryGetValue("Mask", out var mask);
        map.TryGetValue("bs-frame", out var bsFrame);
        map.TryGetValue("thumb-frame", out var thumbFrame);
        map.TryGetValue("Score", out var scoreText);

        BoundingBox? bbox = null;
        if (map.TryGetValue("BBox", out var bboxText))
        {
            bbox = ParseBoundingBoxValue(bboxText);
        }

        return TryBuildFaceEvent(
            camera,
            receivedAtUtc,
            maxEventTimeSkewMinutes,
            featureValue,
            featureVersion,
            l2Norm,
            startTime,
            ageText,
            gender,
            mask,
            null,
            bsFrame,
            thumbFrame,
            scoreText,
            bbox,
            out faceEvent);
    }

    private static bool TryBuildFaceEvent(
        CameraMetadata camera,
        DateTimeOffset receivedAtUtc,
        int maxEventTimeSkewMinutes,
        string? featureValue,
        string? featureVersion,
        string? l2NormText,
        string? startTimeText,
        string? ageText,
        string? gender,
        string? mask,
        string? faceImageBase64,
        string? bsFrame,
        string? thumbFrame,
        string? scoreText,
        BoundingBox? bbox,
        out FaceEvent faceEvent)
    {
        faceEvent = new FaceEvent();
        if (string.IsNullOrWhiteSpace(featureValue))
        {
            return false;
        }

        if (!TryDecodeFeatureValue(featureValue, out var bytes, out var vector))
        {
            return false;
        }

        var l2Norm = ParseFloat(l2NormText);
        if (l2Norm.HasValue && l2Norm.Value > 0.0001f)
        {
            if (MathF.Abs(l2Norm.Value - 1f) > 0.0001f)
            {
                var inv = 1f / l2Norm.Value;
                for (var i = 0; i < vector.Length; i++)
                {
                    vector[i] *= inv;
                }
            }
        }
        else
        {
            VectorMath.NormalizeInPlace(vector);
            l2Norm = 1f;
        }

        var eventTime = receivedAtUtc;
        if (!string.IsNullOrWhiteSpace(startTimeText) && DateTimeOffset.TryParse(startTimeText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            eventTime = parsed.ToUniversalTime();
        }

        if (maxEventTimeSkewMinutes > 0)
        {
            var maxSkew = TimeSpan.FromMinutes(maxEventTimeSkewMinutes);
            if ((eventTime - receivedAtUtc).Duration() > maxSkew)
            {
                eventTime = receivedAtUtc;
            }
        }

        faceEvent = new FaceEvent
        {
            CameraId = camera.CameraId,
            CameraIp = camera.IpAddress,
            CameraCode = camera.CameraCode,
            CameraName = camera.CameraName,
            EventTimeUtc = eventTime,
            FeatureBytes = bytes,
            FeatureVector = vector,
            L2Norm = l2Norm ?? 0f,
            FeatureVersion = featureVersion ?? string.Empty,
            Age = ParseInt(ageText),
            Gender = gender,
            Mask = mask,
            FaceImageBase64 = faceImageBase64,
            BsFrame = bsFrame,
            ThumbFrame = thumbFrame,
            Score = ParseFloat(scoreText),
            BBox = bbox
        };

        return true;
    }

    private static bool TryDecodeFeatureValue(string base64, out byte[] bytes, out float[] vector)
    {
        bytes = Array.Empty<byte>();
        vector = Array.Empty<float>();

        try
        {
            if (TryDecodeBase64Vector(base64, out bytes, out vector))
            {
                return true;
            }

            if (TryDecodeHexVector(base64, out bytes, out vector))
            {
                return true;
            }

            if (TryDecodeFloatListVector(base64, out bytes, out vector))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDecodeBase64Vector(string raw, out byte[] bytes, out float[] vector)
    {
        bytes = Array.Empty<byte>();
        vector = Array.Empty<float>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim().Trim('"');
        var base64Index = trimmed.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        if (base64Index >= 0)
        {
            trimmed = trimmed.Substring(base64Index + "base64,".Length);
        }

        if (trimmed.Contains('%', StringComparison.Ordinal))
        {
            try
            {
                trimmed = Uri.UnescapeDataString(trimmed);
            }
            catch
            {
            }
        }

        trimmed = trimmed.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (trimmed.Length == 0)
        {
            return false;
        }

        if (TryDecodeBase64(trimmed, out bytes, out vector))
        {
            return true;
        }

        var urlSafe = trimmed.Replace('-', '+').Replace('_', '/');
        return TryDecodeBase64(urlSafe, out bytes, out vector);
    }

    private static bool TryDecodeBase64(string base64, out byte[] bytes, out float[] vector)
    {
        bytes = Array.Empty<byte>();
        vector = Array.Empty<float>();

        var padded = base64;
        var mod = padded.Length % 4;
        if (mod != 0)
        {
            padded = padded.PadRight(padded.Length + (4 - mod), '=');
        }

        try
        {
            bytes = Convert.FromBase64String(padded);
        }
        catch
        {
            return false;
        }

        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
        {
            return false;
        }

        vector = MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        return true;
    }

    private static bool TryDecodeHexVector(string raw, out byte[] bytes, out float[] vector)
    {
        bytes = Array.Empty<byte>();
        vector = Array.Empty<float>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim().Trim('"');
        trimmed = trimmed.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (trimmed.Length < 8 || trimmed.Length % 2 != 0)
        {
            return false;
        }

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        bytes = new byte[trimmed.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(trimmed.Substring(i * 2, 2), 16);
        }

        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
        {
            return false;
        }

        vector = MemoryMarshal.Cast<byte, float>(bytes).ToArray();
        return true;
    }

    private static bool TryDecodeFloatListVector(string raw, out byte[] bytes, out float[] vector)
    {
        bytes = Array.Empty<byte>();
        vector = Array.Empty<float>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim().Trim('"');
        if (!trimmed.Contains(',', StringComparison.Ordinal) && !trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = trimmed.Split(new[] { ',', ' ', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return false;
        }

        var values = new List<float>(parts.Length);
        foreach (var part in parts)
        {
            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }
            values.Add(parsed);
        }

        vector = values.ToArray();
        bytes = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
        return true;
    }

    private static int? ParseInt(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    private static float? ParseFloat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        if (trimmed.Contains(',', StringComparison.Ordinal) && !trimmed.Contains('.', StringComparison.Ordinal))
        {
            var normalized = trimmed.Replace(',', '.');
            if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }
        }

        if (TryDecodeBase64Float(trimmed, out result))
        {
            return result;
        }

        return null;
    }

    private static bool TryParseBoundingBox(XmlReader reader, out BoundingBox bbox)
    {
        bbox = default;
        var x = reader.GetAttribute("x") ?? reader.GetAttribute("X") ?? reader.GetAttribute("left") ?? reader.GetAttribute("Left");
        var y = reader.GetAttribute("y") ?? reader.GetAttribute("Y") ?? reader.GetAttribute("top") ?? reader.GetAttribute("Top");
        var w = reader.GetAttribute("width") ?? reader.GetAttribute("w") ?? reader.GetAttribute("Width");
        var h = reader.GetAttribute("height") ?? reader.GetAttribute("h") ?? reader.GetAttribute("Height");

        if (!string.IsNullOrWhiteSpace(x)
            && !string.IsNullOrWhiteSpace(y)
            && !string.IsNullOrWhiteSpace(w)
            && !string.IsNullOrWhiteSpace(h)
            && float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var xf)
            && float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var yf)
            && float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out var wf)
            && float.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out var hf))
        {
            bbox = new BoundingBox(xf, yf, wf, hf);
            return true;
        }

        var right = reader.GetAttribute("right") ?? reader.GetAttribute("Right");
        var bottom = reader.GetAttribute("bottom") ?? reader.GetAttribute("Bottom");
        if (!string.IsNullOrWhiteSpace(x)
            && !string.IsNullOrWhiteSpace(y)
            && !string.IsNullOrWhiteSpace(right)
            && !string.IsNullOrWhiteSpace(bottom)
            && float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftValue)
            && float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var topValue)
            && float.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightValue)
            && float.TryParse(bottom, NumberStyles.Float, CultureInfo.InvariantCulture, out var bottomValue))
        {
            var minX = Math.Min(leftValue, rightValue);
            var minY = Math.Min(topValue, bottomValue);
            var width = Math.Abs(rightValue - leftValue);
            var height = Math.Abs(bottomValue - topValue);
            bbox = new BoundingBox(minX, minY, width, height);
            return true;
        }

        return false;
    }

    private static BoundingBox? ParseBoundingBoxValue(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return null;
        }

        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
        {
            if (w < 0f || h < 0f)
            {
                var minX = Math.Min(x, w);
                var minY = Math.Min(y, h);
                return new BoundingBox(minX, minY, Math.Abs(w - x), Math.Abs(h - y));
            }

            return new BoundingBox(x, y, w, h);
        }

        return null;
    }

    private static string? ExtractSimpleElementValue(string xml, string elementName)
    {
        var startTag = "<" + elementName + ">";
        var endTag = "</" + elementName + ">";

        var startIndex = xml.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += startTag.Length;
        var endIndex = xml.IndexOf(endTag, startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0 || endIndex <= startIndex)
        {
            return null;
        }

        return xml.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private static bool TryDecodeBase64Float(string value, out float result)
    {
        result = 0f;
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != sizeof(float))
            {
                return false;
            }

            result = MemoryMarshal.Cast<byte, float>(bytes)[0];
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ParseAgeText(string ageXml)
    {
        if (string.IsNullOrWhiteSpace(ageXml))
        {
            return null;
        }

        try
        {
            var normalized = NormalizeXmlFragment(ageXml, "Age", "Range");
            var element = XElement.Parse(normalized);
            var ranges = element.Descendants()
                .Where(node => string.Equals(node.Name.LocalName, "Range", StringComparison.OrdinalIgnoreCase))
                .Select(node =>
                {
                    var min = ParseInt(node.Attribute("min")?.Value);
                    var max = ParseInt(node.Attribute("max")?.Value);
                    var score = ParseFloat(node.Value);
                    return (Min: min, Max: max, Score: score);
                })
                .Where(entry => entry.Score.HasValue)
                .Select(entry => (entry.Min, entry.Max, Score: entry.Score!.Value))
                .ToList();

            if (ranges.Count > 0)
            {
                var best = ranges.OrderByDescending(entry => entry.Score).First();
                var age = best.Min.HasValue && best.Max.HasValue
                    ? (best.Min.Value + best.Max.Value) / 2
                    : best.Min ?? best.Max;
                if (age.HasValue)
                {
                    return age.Value.ToString(CultureInfo.InvariantCulture);
                }
            }

            var value = element.Value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            var trimmed = ageXml.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }

    private static string? ParseGenderText(string genderXml)
    {
        if (string.IsNullOrWhiteSpace(genderXml))
        {
            return null;
        }

        try
        {
            var normalized = NormalizeXmlFragment(genderXml, "Gender", "Male", "Female");
            var element = XElement.Parse(normalized);
            float? male = null;
            float? female = null;

            foreach (var node in element.Descendants())
            {
                var name = node.Name.LocalName;
                if (string.Equals(name, "Male", StringComparison.OrdinalIgnoreCase))
                {
                    male = ParseFloat(node.Value);
                }
                else if (string.Equals(name, "Female", StringComparison.OrdinalIgnoreCase))
                {
                    female = ParseFloat(node.Value);
                }
            }

            if (male.HasValue || female.HasValue)
            {
                if (!female.HasValue || (male.HasValue && male.Value >= female.Value))
                {
                    return "Male";
                }

                return "Female";
            }

            var value = element.Value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            var trimmed = genderXml.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }

    private static string NormalizeXmlFragment(string value, string rootName, params string[] markers)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("<" + rootName, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        foreach (var marker in markers)
        {
            if (trimmed.IndexOf("<" + marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"<{rootName}>{trimmed}</{rootName}>";
            }
        }

        return trimmed;
    }

    private static void SetItem(IDictionary<string, string> items, string? name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var key = NormalizeKey(name);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        items[key] = value;
    }

    private static string? GetItem(IReadOnlyDictionary<string, string> items, params string[] names)
    {
        foreach (var name in names)
        {
            var key = NormalizeKey(name);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (items.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}

