using BluetoothAutoReconnect.Host;
using BluetoothAutoReconnect.Model;

using Microsoft.Extensions.Logging;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace BluetoothAutoReconnect.Devices;

/// <summary>
/// Passive LE advertisement watcher for managed devices. Passive scanning sends nothing over the air,
/// so it is safe to keep running (unlike inquiry). Interop — verified by hand.
/// </summary>
internal sealed class AdvertisementMonitor : IDisposable
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(1);

    private readonly ManagedDeviceStore _store;
    private readonly ILogger<AdvertisementMonitor> _logger;
    private readonly BluetoothLEAdvertisementWatcher _watcher = new() { ScanningMode = BluetoothLEScanningMode.Passive };
    private readonly Dictionary<BluetoothAddress, AdvertisementInfo> _lastRaised = [];
    private readonly Lock _lock = new();
    private int _pauseCount;

    public AdvertisementMonitor(ManagedDeviceStore store, ILogger<AdvertisementMonitor> logger)
    {
        _store = store;
        _logger = logger;
        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;
    }

    /// <summary>Raised from a WinRT thread, at most once per second per device unless its mode changes.</summary>
    public event Action<AdvertisementInfo>? AdvertisementSeen;

    /// <summary>
    /// Starts the watcher, or restarts it after the radio stopped it. Safe to call repeatedly.
    /// Does nothing while paused.
    /// </summary>
    public void EnsureStarted()
    {
        lock (_lock)
        {
            if (_pauseCount == 0
                && _watcher.Status is BluetoothLEAdvertisementWatcherStatus.Created
                    or BluetoothLEAdvertisementWatcherStatus.Stopped
                    or BluetoothLEAdvertisementWatcherStatus.Aborted)
            {
                _watcher.Start();
                _logger.LogInformation("Advertisement watcher started (passive)");
            }
        }
    }

    /// <summary>Stops scanning until the returned handle is disposed, so it cannot interfere with pairing.</summary>
    public IDisposable Pause()
    {
        lock (_lock)
        {
            if (_pauseCount++ == 0 && _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _watcher.Stop();
                _logger.LogInformation("Advertisement watcher paused");
            }
        }

        return new Resumer(this);
    }

    private void Resume()
    {
        lock (_lock)
        {
            _pauseCount--;
        }

        EnsureStarted();
    }

    private sealed class Resumer(AdvertisementMonitor owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Resume();
            }
        }
    }

    public void Dispose()
    {
        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Stop();
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        // Scan responses carry no flags and alternate with the real advertisement; treating them as a
        // mode change flooded the log.
        if (args.AdvertisementType == BluetoothLEAdvertisementType.ScanResponse)
        {
            return;
        }

        var address = new BluetoothAddress(args.BluetoothAddress);
        if (!_store.Contains(address))
        {
            return;
        }

        var flags = args.Advertisement.Flags ?? BluetoothLEAdvertisementFlags.None;
        var info = new AdvertisementInfo(
            address,
            args.BluetoothAddressType == BluetoothAddressType.Public,
            args.AdvertisementType.ToString(),
            args.IsConnectable,
            (flags & (BluetoothLEAdvertisementFlags.GeneralDiscoverableMode
                | BluetoothLEAdvertisementFlags.LimitedDiscoverableMode)) != 0,
            args.RawSignalStrengthInDBm,
            args.Timestamp);

        bool modeChanged;
        lock (_lock)
        {
            _lastRaised.TryGetValue(address, out var last);
            modeChanged = last is null
                || last.Kind != info.Kind
                || last.IsConnectable != info.IsConnectable
                || last.IsDiscoverable != info.IsDiscoverable;
            if (!modeChanged && info.Timestamp - last!.Timestamp < RepeatInterval)
            {
                return;
            }

            _lastRaised[address] = info;
        }

        _logger.Log(
            modeChanged ? LogLevel.Information : LogLevel.Debug,
            "Advertisement from {Address} ({AddressType}): {Kind}, connectable={Connectable}, discoverable={Discoverable}, flags={Flags}, rssi={Rssi}",
            address, args.BluetoothAddressType, info.Kind, info.IsConnectable, info.IsDiscoverable, flags, info.SignalStrength);

        AdvertisementSeen?.Invoke(info);
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args) =>
        _logger.LogWarning("Advertisement watcher stopped: {Error}", args.Error);
}
