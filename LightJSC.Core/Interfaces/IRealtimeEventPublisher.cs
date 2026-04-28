using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IRealtimeEventPublisher
{
    bool TryPublish(FaceMatchDecision decision);
}
