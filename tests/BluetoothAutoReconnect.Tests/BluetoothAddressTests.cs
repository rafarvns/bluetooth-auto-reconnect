using BluetoothAutoReconnect.Model;

namespace BluetoothAutoReconnect.Tests;

public class BluetoothAddressTests
{
    [Theory]
    [InlineData("123456789abc")]
    [InlineData("12:34:56:78:9A:BC")]
    [InlineData("12-34-56-78-9a-bc")]
    public void ParsesCommonFormats(string text)
    {
        Assert.True(BluetoothAddress.TryParse(text, out var address));
        Assert.Equal(0x123456789ABCUL, address.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789a")]
    [InlineData("zz3456789abc")]
    public void RejectsInvalid(string? text) => Assert.False(BluetoothAddress.TryParse(text, out _));

    [Fact]
    public void FormatsForDisplayAndInstanceId()
    {
        var address = BluetoothAddress.Parse("123456789abc");
        Assert.Equal("12:34:56:78:9A:BC", address.ToString());
        Assert.Equal("123456789ABC", address.ToCompactString());
    }
}
