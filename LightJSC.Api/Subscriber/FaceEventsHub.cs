using LightJSC.Core.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Subscriber;

public sealed class FaceEventsHub : Hub
{
    private readonly FaceEventBuffer _buffer;
    private readonly IOptionsMonitor<SubscriberServiceOptions> _options;

    public FaceEventsHub(FaceEventBuffer buffer, IOptionsMonitor<SubscriberServiceOptions> options)
    {
        _buffer = buffer;
        _options = options;
    }

    public override async Task OnConnectedAsync()
    {
        if (!_options.CurrentValue.Enabled)
        {
            Context.Abort();
            return;
        }

        await Clients.Caller.SendAsync("snapshot", _buffer.GetSnapshot());
        await base.OnConnectedAsync();
    }
}
