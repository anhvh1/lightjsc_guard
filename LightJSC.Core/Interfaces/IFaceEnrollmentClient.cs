using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IFaceEnrollmentClient
{
    Task<FaceEnrollmentResult> EnrollAsync(
        string cameraIpAddress,
        string username,
        string password,
        byte[] imageJpeg,
        CancellationToken cancellationToken);
}

