namespace LightJSC.Core.Options;

public sealed class PipelineWatchdogOptions
{
    public int IntervalSeconds { get; set; } = 15;
    public int QueueLogIntervalSeconds { get; set; } = 60;
    public int StallTimeoutSeconds { get; set; } = 120;
    public int StallGraceSeconds { get; set; } = 30;
    public int MaxStallCount { get; set; } = 2;
    public bool AutoRestartOnStall { get; set; } = true;
}
