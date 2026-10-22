using BluetoothAutoReconnect.Model;
using BluetoothAutoReconnect.Reconnect;

namespace BluetoothAutoReconnect.Tests;

public class AdvertisementReactorTests
{
    private static readonly BluetoothAddress Address = BluetoothAddress.Parse("123456789abc");
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static AdvertisementReactor NewReactor() => new(
        new BackoffPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), MaxAttempts: 3),
        repairGrace: TimeSpan.FromSeconds(5));

    private static BluetoothDeviceState State(bool paired, bool connected) =>
        new(Address, "Xbox Wireless Controller", paired, connected, BatteryPercent: null, LastConnected: null);

    private static AdvertisementInfo Ad(double seconds, bool discoverable, bool connectable = true) =>
        new(Address, IsPublicAddress: true, "ConnectableUndirected", connectable, discoverable, -60, T0.AddSeconds(seconds));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConnectedDeviceIsIgnored(bool discoverable) =>
        Assert.Equal(ReactionAction.Ignore,
            NewReactor().OnAdvertisement(State(paired: true, connected: true), Ad(0, discoverable)));

    [Fact]
    public void UnpairedDeviceInPairingModeIsLeftToTheUser()
    {
        // The user may be pairing it from Windows Settings; competing makes both fail.
        var reactor = NewReactor();

        Assert.Equal(ReactionAction.Ignore, reactor.OnAdvertisement(null, Ad(0, discoverable: true)));
        Assert.Equal(ReactionAction.Ignore,
            reactor.OnAdvertisement(State(paired: false, connected: false), Ad(10, discoverable: true)));
    }

    [Fact]
    public void PairedDeviceInPairingModeGetsGracePeriodBeforeRepair()
    {
        var reactor = NewReactor();
        var paired = State(paired: true, connected: false);

        Assert.Equal(ReactionAction.Ignore, reactor.OnAdvertisement(paired, Ad(0, discoverable: true)));
        Assert.Equal(ReactionAction.Ignore, reactor.OnAdvertisement(paired, Ad(4, discoverable: true)));
        Assert.Equal(ReactionAction.Repair, reactor.OnAdvertisement(paired, Ad(5, discoverable: true)));
    }

    [Fact]
    public void RepairsOncePerPairingModeSession()
    {
        var reactor = NewReactor();
        var paired = State(paired: true, connected: false);

        reactor.OnAdvertisement(paired, Ad(0, discoverable: true));
        Assert.Equal(ReactionAction.Repair, reactor.OnAdvertisement(paired, Ad(5, discoverable: true)));
        Assert.Equal(ReactionAction.Ignore, reactor.OnAdvertisement(paired, Ad(120, discoverable: true)));

        // Leaving pairing mode and entering it again starts a new session.
        reactor.OnAdvertisement(paired, Ad(130, discoverable: false));
        reactor.OnAdvertisement(paired, Ad(140, discoverable: true));
        Assert.Equal(ReactionAction.Repair, reactor.OnAdvertisement(paired, Ad(145, discoverable: true)));
    }

    [Fact]
    public void NormalAdvertisementFromPairedDeviceTriggersConnectWithBackoff()
    {
        var reactor = NewReactor();
        var paired = State(paired: true, connected: false);

        Assert.Equal(ReactionAction.ConnectAttempt, reactor.OnAdvertisement(paired, Ad(0, discoverable: false)));
        Assert.Equal(ReactionAction.Ignore, reactor.OnAdvertisement(paired, Ad(1, discoverable: false)));
        // Second attempt waits DelayFor(1) = 4s after the first.
        Assert.Equal(ReactionAction.Ignore, reactor.OnAdvertisement(paired, Ad(3, discoverable: false)));
        Assert.Equal(ReactionAction.ConnectAttempt, reactor.OnAdvertisement(paired, Ad(4, discoverable: false)));
    }

    [Fact]
    public void ConnectAttemptsGiveUpAfterMaxAttempts()
    {
        var reactor = NewReactor();
        var paired = State(paired: true, connected: false);

        Assert.Equal(ReactionAction.ConnectAttempt, reactor.OnAdvertisement(paired, Ad(0, discoverable: false)));
        Assert.Equal(ReactionAction.ConnectAttempt, reactor.OnAdvertisement(paired, Ad(100, discoverable: false)));
        Assert.Equal(ReactionAction.ConnectAttempt, reactor.OnAdvertisement(paired, Ad(200, discoverable: false)));
        Assert.Equal(ReactionAction.Ignore, reactor.OnAdvertisement(paired, Ad(300, discoverable: false)));
    }

    [Fact]
    public void ResetClearsBackoff()
    {
        var reactor = NewReactor();
        var paired = State(paired: true, connected: false);
        reactor.OnAdvertisement(paired, Ad(0, discoverable: false));

        reactor.Reset(Address);

        Assert.Equal(ReactionAction.ConnectAttempt, reactor.OnAdvertisement(paired, Ad(1, discoverable: false)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NormalAdvertisementIsIgnoredWhenUnpairedOrNotConnectable(bool paired, bool connectable) =>
        Assert.Equal(ReactionAction.Ignore,
            NewReactor().OnAdvertisement(State(paired, connected: false), Ad(0, discoverable: false, connectable)));
}
