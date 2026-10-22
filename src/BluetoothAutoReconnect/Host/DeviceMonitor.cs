using System.Threading.Channels;

using BluetoothAutoReconnect.Devices;
using BluetoothAutoReconnect.Model;
using BluetoothAutoReconnect.Reconnect;
using BluetoothAutoReconnect.Tray;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BluetoothAutoReconnect.Host;

/// <summary>
/// Polls managed devices, keeps the reconnect strategies engaged, reacts to advertisements
/// (connect attempt or automatic re-pair) and pushes state to the tray.
/// Events trigger an immediate poll instead of waiting for the tick.
/// </summary>
internal sealed class DeviceMonitor(
    IDeviceSource deviceSource,
    ManagedDeviceStore store,
    MaintainConnectionStrategy maintainConnection,
    AdvertisementConnectStrategy advertisementConnect,
    AdvertisementMonitor advertisements,
    AdvertisementReactor reactor,
    PairingService pairing,
    IOptions<AgentOptions> options,
    TrayApplicationContext tray,
    SynchronizationContext uiContext,
    ILogger<DeviceMonitor> logger) : BackgroundService
{
    // Capacity 1 + DropWrite coalesces bursts of events into a single wake-up.
    private readonly Channel<bool> _wakeUp = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly Channel<AdvertisementInfo> _advertisements = Channel.CreateBounded<AdvertisementInfo>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    // Guards the reactor and the per-device "last action" shared by both loops.
    private readonly Lock _gate = new();
    private readonly Dictionary<BluetoothAddress, string> _lastAction = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (store.Addresses.Count == 0)
        {
            logger.LogWarning("No managed devices yet; pair one from the tray menu");
        }
        else
        {
            logger.LogInformation("Monitoring {Addresses}", store.Addresses);
        }

        maintainConnection.ConnectionChanged += OnWakeUp;
        store.Changed += OnStoreChanged;
        advertisements.AdvertisementSeen += OnAdvertisement;
        pairing.Paired += OnPaired;
        var advertisementLoop = ProcessAdvertisementsAsync(stoppingToken);
        try
        {
            var lastStates = new Dictionary<BluetoothAddress, BluetoothDeviceState?>();
            var disconnectedAt = new Dictionary<BluetoothAddress, DateTimeOffset>();
            while (true)
            {
                TryStartWatcher();
                foreach (var address in store.Addresses)
                {
                    try
                    {
                        var state = await deviceSource.GetStateAsync(address, stoppingToken);
                        var isFirstRead = !lastStates.TryGetValue(address, out var previous);
                        LogChange(address, isFirstRead, previous, state, disconnectedAt);
                        lastStates[address] = state;
                        uiContext.Post(_ => tray.Update(state), null);

                        if (state is { IsPaired: true })
                        {
                            await maintainConnection.AttemptAsync(state, stoppingToken);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "Failed to poll {Address}", address);
                    }
                }

                await WaitForTickOrWakeUpAsync(options.Value.PollInterval, stoppingToken);
            }
        }
        finally
        {
            maintainConnection.ConnectionChanged -= OnWakeUp;
            store.Changed -= OnStoreChanged;
            advertisements.AdvertisementSeen -= OnAdvertisement;
            pairing.Paired -= OnPaired;
            _advertisements.Writer.TryComplete();
            await advertisementLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private void OnWakeUp(BluetoothAddress address) => _wakeUp.Writer.TryWrite(true);

    private void OnStoreChanged() => _wakeUp.Writer.TryWrite(true);

    private void OnAdvertisement(AdvertisementInfo ad) => _advertisements.Writer.TryWrite(ad);

    private void OnPaired(BluetoothAddress address)
    {
        // The old GATT session belonged to the dropped bond.
        maintainConnection.Reset(address);
        SetLastAction(address, StrategyNames.Repair);
        store.Add(address);
        _wakeUp.Writer.TryWrite(true);
    }

    private void TryStartWatcher()
    {
        try
        {
            advertisements.EnsureStarted();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start advertisement watcher");
        }
    }

    private async Task ProcessAdvertisementsAsync(CancellationToken stoppingToken)
    {
        await foreach (var ad in _advertisements.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var state = await deviceSource.GetStateAsync(ad.Address, stoppingToken);
                ReactionAction action;
                lock (_gate)
                {
                    action = reactor.OnAdvertisement(state, ad);
                }

                switch (action)
                {
                    case ReactionAction.ConnectAttempt when state is not null:
                        SetLastAction(ad.Address, advertisementConnect.Name);
                        await advertisementConnect.AttemptAsync(state, stoppingToken);
                        break;
                    case ReactionAction.Repair:
                        await RepairAsync(ad.Address, ad.IsPublicAddress, stoppingToken);
                        break;
                    default:
                        continue;
                }

                _wakeUp.Writer.TryWrite(true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to react to advertisement from {Address}", ad.Address);
            }
        }
    }

    private async Task RepairAsync(BluetoothAddress address, bool isPublicAddress, CancellationToken stoppingToken)
    {
        logger.LogInformation("Device {Address} is in pairing mode with no working bond; re-pairing", address);
        SetLastAction(address, StrategyNames.Repair);
        Notify("Pareando o controle…", "Controle em modo de pareamento detectado.", ToolTipIcon.Info);

        var result = await pairing.RepairAsync(address, isPublicAddress, stoppingToken);
        if (result.Succeeded)
        {
            Notify("Controle pareado", "Pareado de novo automaticamente.", ToolTipIcon.Info);
        }
        else
        {
            Notify("Falha ao parear", $"O Windows respondeu: {result.Status}. Tente de novo pelo menu.", ToolTipIcon.Warning);
        }
    }

    private void Notify(string title, string text, ToolTipIcon icon) =>
        uiContext.Post(_ => tray.ShowNotification(title, text, icon), null);

    private void SetLastAction(BluetoothAddress address, string strategy)
    {
        lock (_gate)
        {
            _lastAction[address] = strategy;
        }
    }

    private async Task WaitForTickOrWakeUpAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(interval);
        try
        {
            await _wakeUp.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Regular poll tick.
        }
    }

    private void LogChange(
        BluetoothAddress address,
        bool isFirstRead,
        BluetoothDeviceState? previous,
        BluetoothDeviceState? current,
        Dictionary<BluetoothAddress, DateTimeOffset> disconnectedAt)
    {
        if (!isFirstRead && previous == current)
        {
            return;
        }

        logger.LogInformation("Device {Address} state: {@State}", address, current);

        switch (ConnectionTransitions.Classify(previous, current))
        {
            case ConnectionTransition.Disconnected:
                disconnectedAt[address] = DateTimeOffset.UtcNow;
                break;
            case ConnectionTransition.Connected:
                string strategy;
                lock (_gate)
                {
                    reactor.Reset(address);
                    strategy = _lastAction.Remove(address, out var action) ? action : maintainConnection.Name;
                }

                if (disconnectedAt.Remove(address, out var since))
                {
                    logger.LogInformation(
                        "Device {Address} reconnected after {Elapsed} via {Strategy}",
                        address, DateTimeOffset.UtcNow - since, strategy);
                }
                else
                {
                    logger.LogInformation("Device {Address} connected via {Strategy}", address, strategy);
                }

                break;
            case ConnectionTransition.Unpaired:
                logger.LogWarning(
                    "Device {Address} is no longer paired; pair it again from the tray window or Windows Settings", address);
                break;
        }
    }
}
