using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class DlqRepository : IDlqRepository
{
    private readonly IngestorDbContext _dbContext;

    public DlqRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(DlqMessage message, CancellationToken cancellationToken)
    {
        _dbContext.DlqMessages.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

