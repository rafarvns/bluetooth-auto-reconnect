namespace BluetoothAutoReconnect.Model;

/// <summary>
/// Snapshot of a managed device. Everything downstream of <c>Devices/</c> consumes this,
/// never raw WinRT types.
/// </summary>
public sealed record BluetoothDeviceState(
    BluetoothAddress Address,
    string Name,
    bool IsPaired,
    bool IsConnected,
    byte? BatteryPercent,
    DateTimeOffset? LastConnected);
