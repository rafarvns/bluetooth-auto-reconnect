namespace BluetoothAutoReconnect.Model;

/// <summary>A BLE advertisement from a managed device, stripped of WinRT types.</summary>
/// <param name="IsPublicAddress">Public vs random address; needed to connect to the right identity.</param>
/// <param name="Kind">WinRT advertisement type name, kept for logs (e.g. <c>ConnectableUndirected</c>).</param>
/// <param name="IsDiscoverable">
/// General or limited discoverable flag is set — "the device is in pairing mode". Verified on the
/// controller: <c>GeneralDiscoverableMode</c> appears while the Xbox button blinks fast.
/// </param>
public sealed record AdvertisementInfo(
    BluetoothAddress Address,
    bool IsPublicAddress,
    string Kind,
    bool IsConnectable,
    bool IsDiscoverable,
    short SignalStrength,
    DateTimeOffset Timestamp);
