using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface ISubscriberRepository
{
    Task<IReadOnlyList<Subscriber>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Subscriber subscriber, CancellationToken cancellationToken);
    Task<Subscriber?> UpdateAsync(Subscriber subscriber, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

