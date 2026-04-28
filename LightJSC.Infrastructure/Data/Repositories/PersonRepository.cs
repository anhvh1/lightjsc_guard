using LightJSC.Core.Interfaces;
using LightJSC.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LightJSC.Infrastructure.Data.Repositories;

public sealed class PersonRepository : IPersonRepository
{
    private readonly IngestorDbContext _dbContext;

    public PersonRepository(IngestorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Person?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Persons.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Person?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        return await _dbContext.Persons.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
    }

    public async Task<IReadOnlyList<Person>> ListAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Persons.AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Person person, CancellationToken cancellationToken)
    {
        _dbContext.Persons.Add(person);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Person person, CancellationToken cancellationToken)
    {
        _dbContext.Persons.Update(person);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Persons.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;
        _dbContext.Persons.Update(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteHardAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Persons.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        var templates = await _dbContext.FaceTemplates
            .Where(template => template.PersonId == id)
            .ToListAsync(cancellationToken);

        if (templates.Count > 0)
        {
            _dbContext.FaceTemplates.RemoveRange(templates);
        }

        _dbContext.Persons.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PurgeAsync(bool includeEvents, CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM face_templates;", cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM persons;", cancellationToken);

        try
        {
            const string deleteWatchlistSql = @"
DO $$
DECLARE r record;
BEGIN
  FOR r IN
    SELECT c.relname AS tablename
    FROM pg_class c
    WHERE c.relkind = 'r'
      AND c.relname LIKE 'watchlist_index%'
      AND pg_table_is_visible(c.oid)
  LOOP
    EXECUTE format('DELETE FROM %I', r.tablename);
  END LOOP;
END $$;";
            await _dbContext.Database.ExecuteSqlRawAsync(deleteWatchlistSql, cancellationToken);
        }
        catch (Exception)
        {
        }

        if (includeEvents)
        {
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM face_events;", cancellationToken);
            try
            {
                const string deleteFaceEventIndexSql = @"
DO $$
DECLARE r record;
BEGIN
  FOR r IN
    SELECT c.relname AS tablename
    FROM pg_class c
    WHERE c.relkind = 'r'
      AND c.relname LIKE 'face_event_index%'
      AND pg_table_is_visible(c.oid)
  LOOP
    EXECUTE format('DELETE FROM %I', r.tablename);
  END LOOP;
END $$;";
                await _dbContext.Database.ExecuteSqlRawAsync(deleteFaceEventIndexSql, cancellationToken);
            }
            catch (Exception)
            {
            }
        }
    }
}

