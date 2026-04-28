using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IFaceEventIndex
{
    Task<bool> EnsureSchemaAsync(string? featureVersion, int vectorLength, CancellationToken cancellationToken);
    Task<IReadOnlyList<FaceEventIndexTable>> ListTablesAsync(CancellationToken cancellationToken);
    Task<string?> ResolveTableNameAsync(string? featureVersion, int vectorLength, CancellationToken cancellationToken);
    Task<string?> ResolveTableForEventAsync(Guid eventId, CancellationToken cancellationToken);
    Task UpsertAsync(FaceEventIndexEntry entry, CancellationToken cancellationToken);
    Task DeleteByEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken);
}
