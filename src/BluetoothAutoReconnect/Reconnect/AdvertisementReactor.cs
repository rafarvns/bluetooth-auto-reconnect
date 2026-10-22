using BluetoothAutoReconnect.Model;

namespace BluetoothAutoReconnect.Reconnect;

public enum ReactionAction
{
    Ignore,
    ConnectAttempt,
    Repair,
}

/// <summary>
/// Decides what to do when a managed device advertises. Pure apart from its per-device history;
/// time comes from the advertisement timestamp. Not thread-safe — callers serialize access.
/// </summary>
/// <remarks>
/// Automatic re-pair only replaces a <em>stale</em> bond (Windows says paired, the device is in
/// pairing mode). A device Windows does not have paired is left alone: the user is likely pairing
/// it from Settings or the pairing window right now, and competing with that makes both fail.
/// </remarks>
public sealed class AdvertisementReactor(BackoffPolicy connectBackoff, TimeSpan repairGrace)
{
    private sealed class History
    {
        public int ConnectAttempts { get; set; }
        public DateTimeOffset? LastConnectAttempt { get; set; }
        public DateTimeOffset? DiscoverableSince { get; set; }

        /// <summary>One re-pair per pairing-mode session; cleared when the device leaves pairing mode.</summary>
        public bool RepairedThisSession { get; set; }
    }

    private readonly Dictionary<BluetoothAddress, History> _history = [];

    public static AdvertisementReactor CreateDefault() =>
        new(BackoffPolicy.Default, repairGrace: TimeSpan.FromSeconds(5));

    public ReactionAction OnAdvertisement(BluetoothDeviceState? state, AdvertisementInfo ad)
    {
        if (!_history.TryGetValue(ad.Address, out var history))
        {
            history = new History();
            _history[ad.Address] = history;
        }

        if (!ad.IsDiscoverable)
        {
            history.DiscoverableSince = null;
            history.RepairedThisSession = false;
        }

        // Never touch a working link, and never pair a device Windows does not have paired.
        if (state is not { IsPaired: true, IsConnected: false })
        {
            return ReactionAction.Ignore;
        }

        var now = ad.Timestamp;
        if (ad.IsDiscoverable)
        {
            history.DiscoverableSince ??= now;

            // Give the existing bond a chance to reconnect on its own before declaring it stale.
            if (history.RepairedThisSession || now - history.DiscoverableSince < repairGrace)
            {
                return ReactionAction.Ignore;
            }

            history.RepairedThisSession = true;
            return ReactionAction.Repair;
        }

        if (!ad.IsConnectable
            || connectBackoff.DelayFor(history.ConnectAttempts) is not { } delay
            || (history.LastConnectAttempt is { } last && now - last < delay))
        {
            return ReactionAction.Ignore;
        }

        history.ConnectAttempts++;
        history.LastConnectAttempt = now;
        return ReactionAction.ConnectAttempt;
    }

    /// <summary>Forget backoff and session state, e.g. after the device connected.</summary>
    public void Reset(BluetoothAddress address) => _history.Remove(address);
}
