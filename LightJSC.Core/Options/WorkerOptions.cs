namespace LightJSC.Core.Options;

public sealed class WorkerOptions
{
    public int MatchDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public int WebhookDegreeOfParallelism { get; set; } = 4;
}

