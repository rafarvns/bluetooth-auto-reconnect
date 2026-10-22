using BluetoothAutoReconnect.Model;

using Microsoft.Extensions.Logging;

using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration.Pnp;

namespace BluetoothAutoReconnect.Devices;

/// <summary>
/// WinRT/PnP-backed device source. Interop is verified by hand against the controller, not unit tested.
/// </summary>
internal sealed class WinRtDeviceSource(ILogger<WinRtDeviceSource> logger) : IDeviceSource
{
    public async Task<BluetoothDeviceState?> GetStateAsync(BluetoothAddress address, CancellationToken cancellationToken)
    {
        // Obtaining a BluetoothLEDevice does NOT connect; it only reads the Windows-cached view.
        using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address.Value).AsTask(cancellationToken);
        if (device is null)
        {
            return null;
        }

        var battery = await ReadBatteryAsync(address, cancellationToken);

        return new BluetoothDeviceState(
            address,
            device.Name,
            device.DeviceInformation.Pairing.IsPaired,
            device.ConnectionStatus == BluetoothConnectionStatus.Connected,
            battery,
            LastConnected: null); // TODO: read DEVPKEY_Bluetooth_LastConnectedTime from the same node.
    }

    private async Task<byte?> ReadBatteryAsync(BluetoothAddress address, CancellationToken cancellationToken)
    {
        // TODO: verify against the controller. AQS "~<" is "starts with".
        var aqs = $"{PnpPropertyKeys.DeviceInstanceId}:~<\"BTHLE\\DEV_{address.ToCompactString()}\"";
        try
        {
            var nodes = await PnpObject
                .FindAllAsync(PnpObjectType.Device, [PnpPropertyKeys.Battery], aqs)
                .AsTask(cancellationToken);

            foreach (var node in nodes)
            {
                if (node.Properties.TryGetValue(PnpPropertyKeys.Battery, out var value) && value is byte percent)
                {
                    return percent;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read battery for {Address}", address);
        }

        return null;
    }
}
