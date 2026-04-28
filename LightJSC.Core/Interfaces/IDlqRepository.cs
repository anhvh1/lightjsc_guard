using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IDlqRepository
{
    Task AddAsync(DlqMessage message, CancellationToken cancellationToken);
}

