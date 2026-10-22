using System.Drawing;

using BluetoothAutoReconnect.Tray;

namespace BluetoothAutoReconnect.Tests;

public class GamepadIconRendererTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void RendersAtRequestedSize(int size)
    {
        using var bitmap = GamepadIconRenderer.Render(Color.Red, size);

        Assert.Equal(new Size(size, size), bitmap.Size);
    }

    [Fact]
    public void FillsBodyWithColorAndLeavesCornersTransparent()
    {
        var fill = BatteryPalette.ColorFor(BatteryBucket.Healthy);
        using var bitmap = GamepadIconRenderer.Render(fill, 32);

        // (16, 12) sits in the middle of the body, between the d-pad and the face buttons.
        Assert.Equal(fill.ToArgb(), bitmap.GetPixel(16, 12).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(0, bitmap.GetPixel(31, 0).A);
    }

    [Fact]
    public void HasDarkOutlineAtTheEdge()
    {
        using var bitmap = GamepadIconRenderer.Render(Color.White, 32);

        // Just outside the left edge of the body (x = 3), at mid height.
        var edge = bitmap.GetPixel(2, 15);
        Assert.True(edge.A > 0 && edge.R < 128, $"expected dark outline, got {edge}");
    }
}
