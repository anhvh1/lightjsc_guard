using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class FaceEventRepository : IFaceEventRepository
{
    private readonly IngestorDbContext _dbContext;

    public FaceEventRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FaceEventRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.FaceEvents.AsNoTracking()
            .FirstOrDefaultAsync(record => record.Id == id, cancellationToken);
    }

    public async Task AddAsync(FaceEventRecord record, CancellationToken cancellationToken)
    {
        _dbContext.FaceEvents.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FaceEventRecord>> ListAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.FaceEvents.AsNoTracking();

        if (fromUtc.HasValue)
        {
            query = query.Where(record => record.EventTimeUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(record => record.EventTimeUtc <= toUtc.Value);
        }

        return await query
            .OrderBy(record => record.EventTimeUtc)
            .Take(Math.Max(1, maxCount))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FaceEventRecord>> ListOlderThanAsync(
        DateTime cutoffUtc,
        int maxCount,
        CancellationToken cancellationToken)
    {
        return await _dbContext.FaceEvents.AsNoTracking()
            .Where(record => record.EventTimeUtc < cutoffUtc)
            .OrderBy(record => record.EventTimeUtc)
            .Take(Math.Max(1, maxCount))
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var records = await _dbContext.FaceEvents
            .Where(record => ids.Contains(record.Id))
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            return;
        }

        _dbContext.FaceEvents.RemoveRange(records);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
