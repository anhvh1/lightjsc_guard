using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class FaceTemplateRepository : IFaceTemplateRepository
{
    private readonly IngestorDbContext _dbContext;

    public FaceTemplateRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FaceTemplate?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.FaceTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<FaceTemplate>> ListByPersonAsync(Guid personId, CancellationToken cancellationToken)
    {
        return await _dbContext.FaceTemplates.AsNoTracking()
            .Where(x => x.PersonId == personId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FaceTemplate>> ListActiveByPersonIdsAsync(
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return Array.Empty<FaceTemplate>();
        }

        return await _dbContext.FaceTemplates.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId) && x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsByHashAsync(Guid personId, string featureHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(featureHash))
        {
            return false;
        }

        return await _dbContext.FaceTemplates.AsNoTracking()
            .AnyAsync(x => x.PersonId == personId && x.FeatureHash == featureHash && x.IsActive, cancellationToken);
    }

    public async Task AddAsync(FaceTemplate template, CancellationToken cancellationToken)
    {
        _dbContext.FaceTemplates.Add(template);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FaceTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.IsActive = isActive;
        entity.UpdatedAt = DateTime.UtcNow;
        _dbContext.FaceTemplates.Update(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.FaceTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        _dbContext.FaceTemplates.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

