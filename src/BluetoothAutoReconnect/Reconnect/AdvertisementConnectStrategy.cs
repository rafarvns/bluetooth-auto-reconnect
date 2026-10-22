using BluetoothAutoReconnect.Model;

using Microsoft.Extensions.Logging;

using Windows.Devices.Bluetooth;

namespace BluetoothAutoReconnect.Reconnect;

/// <summary>
/// Strategy 2: on an advertisement from the device, force a real connection attempt. Obtaining a
/// <see cref="BluetoothLEDevice"/> does not connect; an uncached GATT discovery does.
/// Interop — verified by hand.
/// </summary>
internal sealed class AdvertisementConnectStrategy(ILogger<AdvertisementConnectStrategy> logger) : IReconnectStrategy
{
    public string Name => StrategyNames.AdvertisementConnect;

    public bool RequiresUserInteraction => false;

    public async Task AttemptAsync(BluetoothDeviceState device, CancellationToken cancellationToken)
    {
        using var ble = await BluetoothLEDevice.FromBluetoothAddressAsync(device.Address.Value).AsTask(cancellationToken);
        if (ble is null)
        {
            return;
        }

        var result = await ble.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        logger.LogInformation(
            "GATT discovery on {Address}: {Status}, {Count} services, link {Connection}",
            device.Address, result.Status, result.Services.Count, ble.ConnectionStatus);

        foreach (var service in result.Services)
        {
            service.Dispose();
        }
    }
}
