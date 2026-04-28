using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IFaceTemplateRepository
{
    Task<FaceTemplate?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<FaceTemplate>> ListByPersonAsync(Guid personId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FaceTemplate>> ListActiveByPersonIdsAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken);
    Task<bool> ExistsByHashAsync(Guid personId, string featureHash, CancellationToken cancellationToken);
    Task AddAsync(FaceTemplate template, CancellationToken cancellationToken);
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

