namespace LightJSC.Core.Models;

public sealed class AlarmDeliverySettings
{
    public bool SendWhiteList { get; set; } = true;
    public bool SendBlackList { get; set; } = true;
    public bool SendProtect { get; set; } = true;
    public bool SendUndefined { get; set; } = true;

    public static AlarmDeliverySettings Default => new();
}
