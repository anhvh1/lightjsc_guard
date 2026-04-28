using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IFaceEventDeduplicator
{
    bool ShouldProcess(FaceEvent faceEvent);
}

