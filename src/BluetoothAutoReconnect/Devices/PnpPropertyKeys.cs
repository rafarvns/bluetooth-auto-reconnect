namespace BluetoothAutoReconnect.Devices;

/// <summary>
/// PnP property keys verified on the dev machine (see CLAUDE.md, "Verified hardware facts").
/// They live on the <c>BTHLE\DEV_...</c> device node.
/// </summary>
internal static class PnpPropertyKeys
{
    /// <summary>DEVPKEY_Bluetooth_Battery — <c>Byte</c> percentage.</summary>
    public const string Battery = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public const string DeviceInstanceId = "System.Devices.DeviceInstanceId";
}
