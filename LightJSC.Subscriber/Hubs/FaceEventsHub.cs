using LightJSC.Subscriber.Services;
using Microsoft.AspNetCore.SignalR;

namespace LightJSC.Subscriber.Hubs;

public sealed class FaceEventsHub : Hub
{
    private readonly FaceEventBuffer _buffer;

    public FaceEventsHub(FaceEventBuffer buffer)
    {
        _buffer = buffer;
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("snapshot", _buffer.GetSnapshot());
        await base.OnConnectedAsync();
    }
}

