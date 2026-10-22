using BluetoothAutoReconnect.Model;

namespace BluetoothAutoReconnect.Devices;

/// <summary>Reads current device state. The only seam between pure logic and WinRT/PnP interop.</summary>
public interface IDeviceSource
{
    /// <summary>Returns <c>null</c> when Windows does not know the device (not paired).</summary>
    Task<BluetoothDeviceState?> GetStateAsync(BluetoothAddress address, CancellationToken cancellationToken);
}
