using BluetoothAutoReconnect.Model;
using BluetoothAutoReconnect.Reconnect;

namespace BluetoothAutoReconnect.Tests;

public class ConnectionTransitionTests
{
    private static BluetoothDeviceState State(bool connected, bool paired = true) =>
        new(BluetoothAddress.Parse("123456789abc"), "Xbox Wireless Controller",
            paired, connected, BatteryPercent: 76, LastConnected: null);

    [Fact]
    public void DisconnectedToConnectedIsConnected() =>
        Assert.Equal(ConnectionTransition.Connected, ConnectionTransitions.Classify(State(false), State(true)));

    [Fact]
    public void ConnectedToDisconnectedIsDisconnected() =>
        Assert.Equal(ConnectionTransition.Disconnected, ConnectionTransitions.Classify(State(true), State(false)));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameConnectionStateIsNone(bool connected) =>
        Assert.Equal(ConnectionTransition.None, ConnectionTransitions.Classify(State(connected), State(connected)));

    [Fact]
    public void PairedToUnknownIsUnpaired() =>
        Assert.Equal(ConnectionTransition.Unpaired, ConnectionTransitions.Classify(State(true), null));

    [Fact]
    public void PairedToNotPairedIsUnpaired() =>
        Assert.Equal(ConnectionTransition.Unpaired,
            ConnectionTransitions.Classify(State(false), State(false, paired: false)));

    [Fact]
    public void FirstReadOfConnectedDeviceIsNotAReconnect() =>
        Assert.Equal(ConnectionTransition.None, ConnectionTransitions.Classify(null, State(true)));
}
