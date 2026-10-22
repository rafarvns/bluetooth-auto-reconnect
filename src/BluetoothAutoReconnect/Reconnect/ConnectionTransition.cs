using BluetoothAutoReconnect.Model;

namespace BluetoothAutoReconnect.Reconnect;

public enum ConnectionTransition
{
    None,
    Connected,
    Disconnected,
    Unpaired,
}

public static class ConnectionTransitions
{
    /// <summary>
    /// Classifies the change between two consecutive reads. A first read (<paramref name="previous"/>
    /// is <c>null</c>) is never a transition, so an already-connected device does not log a spurious
    /// reconnect at startup.
    /// </summary>
    public static ConnectionTransition Classify(BluetoothDeviceState? previous, BluetoothDeviceState? current)
    {
        if (previous is null)
        {
            return ConnectionTransition.None;
        }

        if (previous.IsPaired && current is not { IsPaired: true })
        {
            return ConnectionTransition.Unpaired;
        }

        return (previous.IsConnected, current?.IsConnected ?? false) switch
        {
            (false, true) => ConnectionTransition.Connected,
            (true, false) => ConnectionTransition.Disconnected,
            _ => ConnectionTransition.None,
        };
    }
}
