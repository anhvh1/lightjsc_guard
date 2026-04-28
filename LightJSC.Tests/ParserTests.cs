using System.Runtime.InteropServices;
using LightJSC.Core.Models;
using LightJSC.Infrastructure.Parsers;
using Xunit;

namespace LightJSC.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ParseXmlMetadataProducesFaceEvent()
    {
        var vector = new[] { 1f, 2f, 3f, 4f };
        var bytes = MemoryMarshal.Cast<float, byte>(vector.AsSpan()).ToArray();
        var base64 = Convert.ToBase64String(bytes);
        var imageBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var l2Norm = MathF.Sqrt(30f);
        var xml = $"<Meta><FeatureValue>{base64}</FeatureValue><L2Norm>{l2Norm}</L2Norm><start-time>2024-01-01T00:00:00Z</start-time><Age>30</Age><Gender>Male</Gender><Mask>0</Mask><Image>{imageBase64}</Image><bs-frame>bs</bs-frame><thumb-frame>th</thumb-frame><feature-value-version>0.1</feature-value-version></Meta>";

        var parser = new FaceMetadataParser();
        var metadata = new CameraMetadata
        {
            CameraId = "CAM01",
            IpAddress = "192.168.1.10"
        };
        var ok = parser.TryParse(metadata, xml, DateTimeOffset.UtcNow, out var faceEvent);

        Assert.True(ok);
        Assert.Equal("CAM01", faceEvent.CameraId);
        Assert.Equal("192.168.1.10", faceEvent.CameraIp);
        Assert.Equal("0.1", faceEvent.FeatureVersion);
        Assert.Equal(30, faceEvent.Age);
        Assert.Equal("Male", faceEvent.Gender);
        Assert.Equal("0", faceEvent.Mask);
        Assert.Equal(imageBase64, faceEvent.FaceImageBase64);
        Assert.Equal("bs", faceEvent.BsFrame);
        Assert.Equal("th", faceEvent.ThumbFrame);
        Assert.Equal(bytes.Length, faceEvent.FeatureBytes.Length);
        Assert.Equal(4, faceEvent.FeatureVector.Length);

        var norm = MathF.Sqrt(faceEvent.FeatureVector.Sum(v => v * v));
        Assert.InRange(norm, 0.99f, 1.01f);
    }

    [Fact]
    public void ParseKeyValueMetadataProducesFaceEvent()
    {
        var vector = new[] { 0.5f, 0.5f };
        var bytes = MemoryMarshal.Cast<float, byte>(vector.AsSpan()).ToArray();
        var base64 = Convert.ToBase64String(bytes);
        var l2Norm = MathF.Sqrt(0.5f);
        var kv = $"FeatureValue={base64} L2Norm={l2Norm} start-time=2024-02-01T01:02:03Z Age=40 Gender=Female Mask=1";

        var parser = new FaceMetadataParser();
        var metadata = new CameraMetadata
        {
            CameraId = "CAM02",
            IpAddress = "192.168.1.11"
        };
        var ok = parser.TryParse(metadata, kv, DateTimeOffset.UtcNow, out var faceEvent);

        Assert.True(ok);
        Assert.Equal("CAM02", faceEvent.CameraId);
        Assert.Equal("192.168.1.11", faceEvent.CameraIp);
        Assert.Equal(40, faceEvent.Age);
        Assert.Equal("Female", faceEvent.Gender);
        Assert.Equal("1", faceEvent.Mask);
        Assert.Equal(bytes.Length, faceEvent.FeatureBytes.Length);
    }

    [Fact]
    public void ParseOnvifSimpleItemMetadataProducesFaceEvent()
    {
        var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var bytes = MemoryMarshal.Cast<float, byte>(vector.AsSpan()).ToArray();
        var base64 = Convert.ToBase64String(bytes);
        var l2Norm = MathF.Sqrt(vector.Sum(v => v * v));
        var xml = $"""
            <tt:MetaDataStream xmlns:tt="http://www.onvif.org/ver10/schema">
              <tt:Event>
                <tt:Message UtcTime="2024-03-01T01:02:03Z">
                  <tt:Data>
                    <tt:SimpleItem Name="FeatureValue" Value="{base64}" />
                    <tt:SimpleItem Name="L2Norm" Value="{l2Norm}" />
                    <tt:SimpleItem Name="Age" Value="22" />
                    <tt:SimpleItem Name="Gender" Value="Male" />
                    <tt:SimpleItem Name="Mask" Value="0" />
                    <tt:SimpleItem Name="feature-value-version" Value="0.1" />
                    <tt:ElementItem Name="bs-frame"><tt:Value>bs</tt:Value></tt:ElementItem>
                  </tt:Data>
                </tt:Message>
              </tt:Event>
            </tt:MetaDataStream>
            """;

        var parser = new FaceMetadataParser();
        var metadata = new CameraMetadata
        {
            CameraId = "CAM03",
            IpAddress = "192.168.1.12"
        };
        var ok = parser.TryParse(metadata, xml, DateTimeOffset.UtcNow, out var faceEvent);

        Assert.True(ok);
        Assert.Equal("CAM03", faceEvent.CameraId);
        Assert.Equal("192.168.1.12", faceEvent.CameraIp);
        Assert.Equal("0.1", faceEvent.FeatureVersion);
        Assert.Equal(22, faceEvent.Age);
        Assert.Equal("Male", faceEvent.Gender);
        Assert.Equal("0", faceEvent.Mask);
        Assert.Equal("bs", faceEvent.BsFrame);
        Assert.Equal(bytes.Length, faceEvent.FeatureBytes.Length);
    }

    [Fact]
    public void ParseBoundingBoxLeftTopRightBottom()
    {
        var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var bytes = MemoryMarshal.Cast<float, byte>(vector.AsSpan()).ToArray();
        var base64 = Convert.ToBase64String(bytes);
        var xml = $"""
            <tt:MetaDataStream xmlns:tt="http://www.onvif.org/ver10/schema">
              <tt:BoundingBox left="-0.10" top="0.26" right="-0.03" bottom="0.11" />
              <tt:SimpleItem Name="FeatureValue" Value="{base64}" />
            </tt:MetaDataStream>
            """;

        var parser = new FaceMetadataParser();
        var metadata = new CameraMetadata
        {
            CameraId = "CAM04",
            IpAddress = "192.168.1.13"
        };
        var ok = parser.TryParse(metadata, xml, DateTimeOffset.UtcNow, out var faceEvent);

        Assert.True(ok);
        Assert.True(faceEvent.BBox.HasValue);
        var bbox = faceEvent.BBox!.Value;
        Assert.InRange(bbox.X, -0.1001f, -0.0999f);
        Assert.InRange(bbox.Y, 0.1099f, 0.1101f);
        Assert.InRange(bbox.Width, 0.0699f, 0.0701f);
        Assert.InRange(bbox.Height, 0.1499f, 0.1501f);
    }
}

