using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IRuntimeStateRepository
{
    Task<RuntimeState?> GetAsync(string key, CancellationToken cancellationToken);
    Task UpsertAsync(RuntimeState state, CancellationToken cancellationToken);
}

