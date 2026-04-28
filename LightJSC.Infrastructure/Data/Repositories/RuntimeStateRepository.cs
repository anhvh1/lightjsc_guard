using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class RuntimeStateRepository : IRuntimeStateRepository
{
    private readonly IngestorDbContext _dbContext;

    public RuntimeStateRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RuntimeState?> GetAsync(string key, CancellationToken cancellationToken)
    {
        return await _dbContext.RuntimeStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
    }

    public async Task UpsertAsync(RuntimeState state, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.RuntimeStates.FirstOrDefaultAsync(x => x.Key == state.Key, cancellationToken);
        if (existing is null)
        {
            _dbContext.RuntimeStates.Add(state);
        }
        else
        {
            existing.Value = state.Value;
            existing.UpdatedAt = state.UpdatedAt;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

