using BluetoothAutoReconnect.Model;
using BluetoothAutoReconnect.Tray;

namespace BluetoothAutoReconnect.Tests;

public class BatteryPaletteTests
{
    [Theory]
    [InlineData(100, BatteryBucket.Healthy)]
    [InlineData(61, BatteryBucket.Healthy)]
    [InlineData(60, BatteryBucket.Medium)]
    [InlineData(30, BatteryBucket.Medium)]
    [InlineData(29, BatteryBucket.Low)]
    [InlineData(10, BatteryBucket.Low)]
    [InlineData(9, BatteryBucket.Critical)]
    [InlineData(0, BatteryBucket.Critical)]
    public void MapsPercentToBucket(byte percent, BatteryBucket expected) =>
        Assert.Equal(expected, BatteryPalette.BucketFor(percent));

    [Fact]
    public void MapsLowBatteryToRed() =>
        Assert.Equal(BatteryBucket.Critical, BatteryPalette.BucketFor((byte)5));

    [Fact]
    public void UnknownBatteryIsDisconnected() =>
        Assert.Equal(BatteryBucket.Disconnected, BatteryPalette.BucketFor((byte?)null));

    [Fact]
    public void DisconnectedDeviceIgnoresCachedBattery()
    {
        var device = new BluetoothDeviceState(
            BluetoothAddress.Parse("123456789abc"), "Xbox Wireless Controller",
            IsPaired: true, IsConnected: false, BatteryPercent: 76, LastConnected: null);

        Assert.Equal(BatteryBucket.Disconnected, BatteryPalette.BucketFor(device));
    }

    [Fact]
    public void EveryBucketHasDistinctColor()
    {
        var colors = Enum.GetValues<BatteryBucket>().Select(BatteryPalette.ColorFor).ToList();
        Assert.Equal(colors.Count, colors.Distinct().Count());
    }
}
