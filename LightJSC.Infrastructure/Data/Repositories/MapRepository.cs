using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class MapRepository : IMapRepository
{
    private readonly IngestorDbContext _dbContext;

    public MapRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<MapLayout>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.MapLayouts.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<MapLayout?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.MapLayouts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(MapLayout map, CancellationToken cancellationToken)
    {
        _dbContext.MapLayouts.Add(map);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(MapLayout map, CancellationToken cancellationToken)
    {
        _dbContext.MapLayouts.Update(map);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var map = await _dbContext.MapLayouts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (map is null)
        {
            return;
        }

        var positions = await _dbContext.MapCameraPositions
            .Where(x => x.MapId == id)
            .ToListAsync(cancellationToken);
        if (positions.Count > 0)
        {
            _dbContext.MapCameraPositions.RemoveRange(positions);
        }

        _dbContext.MapLayouts.Remove(map);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MapCameraPosition>> ListPositionsAsync(Guid mapId, CancellationToken cancellationToken)
    {
        return await _dbContext.MapCameraPositions.AsNoTracking()
            .Where(x => x.MapId == mapId)
            .OrderBy(x => x.CameraId)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplacePositionsAsync(
        Guid mapId,
        IReadOnlyList<MapCameraPosition> positions,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.MapCameraPositions
            .Where(x => x.MapId == mapId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _dbContext.MapCameraPositions.RemoveRange(existing);
        }

        if (positions.Count > 0)
        {
            _dbContext.MapCameraPositions.AddRange(positions);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
