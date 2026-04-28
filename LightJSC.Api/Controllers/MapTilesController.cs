using LightJSC.Api.MapTiles;
using LightJSC.Core.Options;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Controllers;

[ApiController]
[EnableCors("MapCors")]
[Route("maptiles")]
public sealed class MapTilesController : ControllerBase
{
    private readonly MapTileRepository _repository;
    private readonly MapOptions _options;

    public MapTilesController(MapTileRepository repository, IOptions<MapOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    [HttpGet("{z:int}/{x:int}/{y:int}.pbf")]
    public async Task<IActionResult> GetTile(int z, int x, int y, CancellationToken cancellationToken)
    {
        if (!_options.ServeLocalAssets)
        {
            return NotFound();
        }

        var tile = await _repository.GetTileAsync(z, x, y, cancellationToken);
        if (tile is null)
        {
            return NotFound();
        }

        var isGzip = tile.Length > 2 && tile[0] == 0x1F && tile[1] == 0x8B;
        Response.Headers["Content-Encoding"] = isGzip ? "gzip" : "identity";
        Response.Headers["Cache-Control"] = "public, max-age=3600";
        Response.Headers["Vary"] = "Accept-Encoding";
        return File(tile, "application/x-protobuf", enableRangeProcessing: true);
    }
}
