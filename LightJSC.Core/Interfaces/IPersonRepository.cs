using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IPersonRepository
{
    Task<Person?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Person?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<IReadOnlyList<Person>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Person person, CancellationToken cancellationToken);
    Task UpdateAsync(Person person, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteHardAsync(Guid id, CancellationToken cancellationToken);
    Task PurgeAsync(bool includeEvents, CancellationToken cancellationToken);
}

