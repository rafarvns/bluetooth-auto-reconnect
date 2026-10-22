using BluetoothAutoReconnect.Model;

namespace BluetoothAutoReconnect.Reconnect;

/// <summary>
/// One step of the escalation chain. Implementations must not assume success: the caller
/// re-reads device state after every attempt.
/// </summary>
public interface IReconnectStrategy
{
    /// <summary>Stable identifier used in config (ordering / per-device disabling) and logs.</summary>
    string Name { get; }

    /// <summary>Strategies that need the user (e.g. re-pair) cannot run unattended.</summary>
    bool RequiresUserInteraction { get; }

    Task AttemptAsync(BluetoothDeviceState device, CancellationToken cancellationToken);
}
