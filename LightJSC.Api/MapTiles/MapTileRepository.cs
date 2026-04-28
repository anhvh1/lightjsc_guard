using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.MapTiles;

public sealed class MapTileRepository
{
    private readonly string _mbtilesPath;
    private readonly MapTileOptions _options;

    public MapTileRepository(IWebHostEnvironment environment, IOptions<MapTileOptions> options)
    {
        _options = options.Value;
        _mbtilesPath = Path.Combine(environment.ContentRootPath, _options.MbtilesPath);
    }

    public async Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken cancellationToken)
    {
        if (z < _options.MinZoom || z > _options.MaxZoom)
        {
            return null;
        }

        if (!File.Exists(_mbtilesPath))
        {
            return null;
        }

        var tileRow = _options.UseTmsCoordinates
            ? ((1 << z) - 1 - y)
            : y;

        await using var connection = new SqliteConnection($"Data Source={_mbtilesPath};Mode=ReadOnly");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tile_data FROM tiles WHERE zoom_level = @z AND tile_column = @x AND tile_row = @row LIMIT 1";
        command.Parameters.AddWithValue("@z", z);
        command.Parameters.AddWithValue("@x", x);
        command.Parameters.AddWithValue("@row", tileRow);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as byte[];
    }
}
