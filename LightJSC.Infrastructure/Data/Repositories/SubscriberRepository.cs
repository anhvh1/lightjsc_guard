using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class SubscriberRepository : ISubscriberRepository
{
    private readonly IngestorDbContext _dbContext;

    public SubscriberRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Subscribers.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        _dbContext.Subscribers.Add(subscriber);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Subscriber?> UpdateAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Subscribers.FirstOrDefaultAsync(x => x.Id == subscriber.Id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Name = subscriber.Name;
        entity.EndpointUrl = subscriber.EndpointUrl;
        entity.Enabled = subscriber.Enabled;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Subscribers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        _dbContext.Subscribers.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

