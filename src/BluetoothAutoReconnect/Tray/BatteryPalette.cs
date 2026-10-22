using BluetoothAutoReconnect.Model;

namespace BluetoothAutoReconnect.Tray;

/// <summary>Battery → icon color mapping. Redraw the icon only when the bucket changes.</summary>
public static class BatteryPalette
{
    public static BatteryBucket BucketFor(BluetoothDeviceState? device) =>
        device is { IsConnected: true } ? BucketFor(device.BatteryPercent) : BatteryBucket.Disconnected;

    public static BatteryBucket BucketFor(byte? batteryPercent) => batteryPercent switch
    {
        null => BatteryBucket.Disconnected,
        > 60 => BatteryBucket.Healthy,
        >= 30 => BatteryBucket.Medium,
        >= 10 => BatteryBucket.Low,
        _ => BatteryBucket.Critical,
    };

    public static Color ColorFor(BatteryBucket bucket) => bucket switch
    {
        BatteryBucket.Healthy => Color.FromArgb(0x2E, 0xCC, 0x40),
        BatteryBucket.Medium => Color.FromArgb(0xF5, 0xC5, 0x18),
        BatteryBucket.Low => Color.FromArgb(0xFF, 0x85, 0x1B),
        BatteryBucket.Critical => Color.FromArgb(0xE8, 0x1E, 0x25),
        _ => Color.FromArgb(0x8A, 0x8A, 0x8A),
    };
}
