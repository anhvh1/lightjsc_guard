namespace LightJSC.Workers.Health;

public sealed class ReadinessState
{
    public bool WatchlistLoaded { get; private set; }
    public bool RegistryRunning { get; private set; }

    public void MarkWatchlistLoaded() => WatchlistLoaded = true;
    public void MarkRegistryRunning() => RegistryRunning = true;
}

