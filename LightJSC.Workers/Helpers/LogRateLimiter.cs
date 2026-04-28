using System.Collections.Concurrent;

namespace LightJSC.Workers.Helpers;

public static class LogRateLimiter
{
    public static bool ShouldLog(ConcurrentDictionary<string, long> timestamps, string key, TimeSpan interval)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        while (true)
        {
            if (!timestamps.TryGetValue(key, out var lastTicks))
            {
                if (timestamps.TryAdd(key, nowTicks))
                {
                    return true;
                }

                continue;
            }

            if (nowTicks - lastTicks < interval.Ticks)
            {
                return false;
            }

            if (timestamps.TryUpdate(key, nowTicks, lastTicks))
            {
                return true;
            }
        }
    }
}
