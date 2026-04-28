using LightJSC.Core.Options;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

[ApiController]
[EnableCors("MapCors")]
[Route("mapstyles")]
public sealed class MapStylesController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly MapOptions _mapOptions;

    public MapStylesController(IWebHostEnvironment environment, IOptions<MapOptions> mapOptions)
    {
        _environment = environment;
        _mapOptions = mapOptions.Value;
    }

    [HttpGet("{*fileName}")]
    public async Task<IActionResult> GetStyle(string fileName)
    {
        if (!_mapOptions.ServeLocalAssets)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest();
        }

        if (fileName.Contains("..") || fileName.Contains('\\'))
        {
            return BadRequest();
        }

        var mapStylesPath = Path.Combine(_environment.ContentRootPath, "MapData", "mapstyles");
        var mapStylesRoot = Path.GetFullPath(mapStylesPath + Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(mapStylesRoot, fileName));

        if (!fullPath.StartsWith(mapStylesRoot, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest();
        }

        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".json")
        {
            var content = await System.IO.File.ReadAllTextAsync(fullPath);
            var baseUrl = $"{Request.Scheme}://{Request.Host.Value}";
            if (content.Contains("/maptiles/"))
            {
                content = content.Replace("/maptiles/", $"{baseUrl}/maptiles/");
            }

            if (content.Contains("/mapstyles/"))
            {
                content = content.Replace("/mapstyles/", $"{baseUrl}/mapstyles/");
            }

            return Content(content, "application/json");
        }

        var contentType = extension switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".json" => "application/json",
            ".pbf" => "application/x-protobuf",
            _ => "application/octet-stream"
        };

        return PhysicalFile(fullPath, contentType);
    }
}
