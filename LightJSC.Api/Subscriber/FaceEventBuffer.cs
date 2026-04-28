using LightJSC.Core.Options;
using Microsoft.Extensions.Options;

namespace LightJSC.Api.Subscriber;

public sealed class FaceEventBuffer
{
    private readonly int _maxItems;
    private readonly List<FaceEventDto> _known = new();
    private readonly List<FaceEventDto> _unknown = new();
    private readonly object _lock = new();
    private long _knownTotal;
    private long _unknownTotal;

    public FaceEventBuffer(IOptions<SubscriberServiceOptions> options)
    {
        _maxItems = Math.Max(10, options.Value.MaxItems);
    }

    public void Add(FaceEventDto faceEvent)
    {
        lock (_lock)
        {
            var list = faceEvent.IsKnown ? _known : _unknown;
            if (faceEvent.IsKnown)
            {
                _knownTotal++;
            }
            else
            {
                _unknownTotal++;
            }
            list.Insert(0, faceEvent);
            Trim(list);
        }
    }

    public FaceEventSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new FaceEventSnapshot
            {
                Known = _known.ToList(),
                Unknown = _unknown.ToList(),
                KnownTotal = _knownTotal,
                UnknownTotal = _unknownTotal
            };
        }
    }

    private void Trim(List<FaceEventDto> list)
    {
        if (list.Count <= _maxItems)
        {
            return;
        }

        list.RemoveRange(_maxItems, list.Count - _maxItems);
    }
}
