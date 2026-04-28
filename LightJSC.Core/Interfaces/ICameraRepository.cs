using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface ICameraRepository
{
    Task<CameraCredential?> GetAsync(string cameraId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CameraCredential>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(CameraCredential camera, CancellationToken cancellationToken);
    Task UpdateAsync(CameraCredential camera, CancellationToken cancellationToken);
    Task DeleteAsync(string cameraId, CancellationToken cancellationToken);
}

