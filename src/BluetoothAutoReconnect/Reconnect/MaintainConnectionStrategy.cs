using BluetoothAutoReconnect.Model;

using Microsoft.Extensions.Logging;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;

namespace BluetoothAutoReconnect.Reconnect;

/// <summary>
/// Strategy 1: hold a <see cref="GattSession"/> with <see cref="GattSession.MaintainConnection"/> set
/// for the agent's lifetime, so Windows re-establishes the link whenever the device advertises again.
/// Interop — verified by hand against the controller, not unit tested.
/// </summary>
internal sealed class MaintainConnectionStrategy(ILogger<MaintainConnectionStrategy> logger)
    : IReconnectStrategy, IDisposable
{
    // Both the device and the session must stay referenced: if the session is collected,
    // MaintainConnection is silently lost.
    private sealed record Holder(
        BluetoothLEDevice Device,
        GattSession Session,
        TypedEventHandler<BluetoothLEDevice, object> OnConnectionStatusChanged,
        TypedEventHandler<GattSession, GattSessionStatusChangedEventArgs> OnSessionStatusChanged);

    private readonly Dictionary<BluetoothAddress, Holder> _holders = [];
    private readonly Dictionary<BluetoothAddress, int> _suspended = [];
    private readonly Lock _lock = new();
    private bool _disposed;

    public string Name => StrategyNames.MaintainConnection;

    public bool RequiresUserInteraction => false;

    /// <summary>Raised from a WinRT thread-pool thread when the link or session status changes.</summary>
    public event Action<BluetoothAddress>? ConnectionChanged;

    /// <summary>Idempotent: a no-op while a session with MaintainConnection is already held.</summary>
    public async Task AttemptAsync(BluetoothDeviceState device, CancellationToken cancellationToken)
    {
        var address = device.Address;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_suspended.ContainsKey(address))
            {
                return;
            }

            // SessionStatus is Closed whenever the device is off — that is the state MaintainConnection
            // is meant to recover from, so it is not a reason to recreate the session.
            if (_holders.TryGetValue(address, out var existing) && existing.Session.MaintainConnection)
            {
                return;
            }
        }

        var bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address.Value).AsTask(cancellationToken);
        if (bleDevice is null)
        {
            logger.LogWarning("Device {Address} not known to Windows; cannot hold a GATT session", address);
            return;
        }

        GattSession session;
        try
        {
            session = await GattSession.FromDeviceIdAsync(bleDevice.BluetoothDeviceId).AsTask(cancellationToken);
            session.MaintainConnection = true;
        }
        catch
        {
            bleDevice.Dispose();
            throw;
        }

        var holder = new Holder(
            bleDevice,
            session,
            (sender, _) =>
            {
                logger.LogInformation("Device {Address} connection status: {Status}", address, sender.ConnectionStatus);
                ConnectionChanged?.Invoke(address);
            },
            (_, args) =>
            {
                logger.LogInformation(
                    "Device {Address} GATT session status: {Status} ({Error})", address, args.Status, args.Error);
                ConnectionChanged?.Invoke(address);
            });

        lock (_lock)
        {
            if (_disposed || _suspended.ContainsKey(address))
            {
                Release(holder);
                return;
            }

            if (_holders.Remove(address, out var stale))
            {
                Release(stale);
            }

            bleDevice.ConnectionStatusChanged += holder.OnConnectionStatusChanged;
            session.SessionStatusChanged += holder.OnSessionStatusChanged;
            _holders[address] = holder;
        }

        logger.LogInformation(
            "Holding GATT session for {Address}: MaintainConnection={Maintain}, status={Status}",
            address, session.MaintainConnection, session.SessionStatus);
    }

    /// <summary>
    /// Drops the session and holds off new ones until the returned handle is disposed. Pairing needs
    /// this: while a MaintainConnection session is held, a pairing request fails in ~100 ms without
    /// even trying to connect (verified 2026-10-21 — the same request with no session paired fine).
    /// </summary>
    public IDisposable Suspend(BluetoothAddress address)
    {
        lock (_lock)
        {
            _suspended[address] = _suspended.GetValueOrDefault(address) + 1;
            if (_holders.Remove(address, out var holder))
            {
                Release(holder);
            }
        }

        return new Resumer(this, address);
    }

    private void Resume(BluetoothAddress address)
    {
        lock (_lock)
        {
            if (--_suspended[address] == 0)
            {
                _suspended.Remove(address);
            }
        }
    }

    private sealed class Resumer(MaintainConnectionStrategy owner, BluetoothAddress address) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Resume(address);
            }
        }
    }

    /// <summary>Drops the held session so the next attempt creates a fresh one (e.g. after re-pairing).</summary>
    public void Reset(BluetoothAddress address)
    {
        lock (_lock)
        {
            if (_holders.Remove(address, out var holder))
            {
                Release(holder);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var holder in _holders.Values)
            {
                Release(holder);
            }

            _holders.Clear();
        }
    }

    private static void Release(Holder holder)
    {
        holder.Device.ConnectionStatusChanged -= holder.OnConnectionStatusChanged;
        holder.Session.SessionStatusChanged -= holder.OnSessionStatusChanged;
        holder.Session.Dispose();
        holder.Device.Dispose();
    }
}
