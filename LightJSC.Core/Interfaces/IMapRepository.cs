using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IMapRepository
{
    Task<IReadOnlyList<MapLayout>> ListAsync(CancellationToken cancellationToken);
    Task<MapLayout?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(MapLayout map, CancellationToken cancellationToken);
    Task UpdateAsync(MapLayout map, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<MapCameraPosition>> ListPositionsAsync(Guid mapId, CancellationToken cancellationToken);
    Task ReplacePositionsAsync(Guid mapId, IReadOnlyList<MapCameraPosition> positions, CancellationToken cancellationToken);
}
