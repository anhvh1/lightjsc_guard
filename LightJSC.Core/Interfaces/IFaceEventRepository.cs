using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IFaceEventRepository
{
    Task<FaceEventRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(FaceEventRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<FaceEventRecord>> ListAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        int maxCount,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<FaceEventRecord>> ListOlderThanAsync(
        DateTime cutoffUtc,
        int maxCount,
        CancellationToken cancellationToken);
    Task DeleteByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
