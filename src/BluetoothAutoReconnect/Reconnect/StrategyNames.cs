namespace BluetoothAutoReconnect.Reconnect;

/// <summary>Escalation chain, cheapest first. See CLAUDE.md, "Reconnect strategies".</summary>
public static class StrategyNames
{
    public const string MaintainConnection = "maintain-connection";
    public const string AdvertisementConnect = "advertisement-connect";
    public const string DeviceNodeRestart = "device-node-restart";
    public const string Repair = "repair";

    public static IReadOnlyList<string> DefaultOrder { get; } =
        [MaintainConnection, AdvertisementConnect, DeviceNodeRestart, Repair];
}
