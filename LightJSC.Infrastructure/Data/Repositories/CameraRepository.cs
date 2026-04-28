using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class CameraRepository : ICameraRepository
{
    private readonly IngestorDbContext _dbContext;

    public CameraRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CameraCredential?> GetAsync(string cameraId, CancellationToken cancellationToken)
    {
        return await _dbContext.Cameras.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CameraId == cameraId, cancellationToken);
    }

    public async Task<IReadOnlyList<CameraCredential>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Cameras.AsNoTracking()
            .OrderBy(x => x.CameraId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(CameraCredential camera, CancellationToken cancellationToken)
    {
        _dbContext.Cameras.Add(camera);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(CameraCredential camera, CancellationToken cancellationToken)
    {
        _dbContext.Cameras.Update(camera);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string cameraId, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Cameras.FirstOrDefaultAsync(x => x.CameraId == cameraId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        _dbContext.Cameras.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

