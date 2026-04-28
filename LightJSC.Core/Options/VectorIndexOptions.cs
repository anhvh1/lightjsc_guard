namespace LightJSC.Core.Options;

public sealed class VectorIndexOptions
{
    public int Dimension { get; set; }
    public int TopK { get; set; } = 20;
    public int HnswM { get; set; } = 16;
    public int HnswEfConstruction { get; set; } = 200;
    public int HnswEfSearch { get; set; } = 64;
    public bool AutoRebuildOnMismatch { get; set; } = true;
    public int QueryTimeoutSeconds { get; set; } = 5;
}
