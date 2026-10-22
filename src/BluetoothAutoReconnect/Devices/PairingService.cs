using BluetoothAutoReconnect.Model;
using BluetoothAutoReconnect.Reconnect;

using Microsoft.Extensions.Logging;

using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace BluetoothAutoReconnect.Devices;

public sealed record PairingResult(bool Succeeded, string Status);

/// <summary>
/// Pairs devices without Windows Settings. One code path for the pairing window and for the
/// automatic re-pair. Operations are serialized — never two pairings at once. Interop — verified by hand.
/// </summary>
internal sealed class PairingService(
    AdvertisementMonitor advertisements, MaintainConnectionStrategy maintainConnection, ILogger<PairingService> logger)
{
    // Windows needs a moment after unpairing before the device can be paired again.
    private static readonly TimeSpan SettleAfterUnpair = TimeSpan.FromSeconds(1);

    // Pause between pairing requests. A request on a device that is not yet connectable takes ~8 s
    // to time out on its own.
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

    // How long to keep requesting. Pairing mode lasts ~2 min; the verified success came on the
    // first request made after the controller entered pairing mode.
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(90);

    // A bond this young is not stale yet; the link may still be coming up.
    private static readonly TimeSpan FreshBond = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<BluetoothAddress, DateTimeOffset> _pairedAt = [];

    /// <summary>Raised after a successful pairing, from whichever thread completed it.</summary>
    public event Action<BluetoothAddress>? Paired;

    /// <summary>
    /// Drops the stale bond (if any) and pairs again. The device must be in pairing mode.
    /// A no-op if the device is connected or was paired moments ago.
    /// </summary>
    /// <param name="isPublicAddress">From the advertisement when known; <c>null</c> lets Windows guess.</param>
    public async Task<PairingResult> RepairAsync(
        BluetoothAddress address, bool? isPublicAddress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var suspended = maintainConnection.Suspend(address);
            using (var existing = await BluetoothLEDevice.FromBluetoothAddressAsync(address.Value).AsTask(cancellationToken))
            {
                var isPaired = existing?.DeviceInformation.Pairing.IsPaired == true;
                if (isPaired && existing!.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    logger.LogInformation("Re-pair of {Address} skipped: already connected", address);
                    return new PairingResult(true, "AlreadyConnected");
                }

                if (isPaired && _pairedAt.TryGetValue(address, out var pairedAt) && DateTimeOffset.UtcNow - pairedAt < FreshBond)
                {
                    logger.LogInformation("Re-pair of {Address} skipped: paired {Age} ago", address, DateTimeOffset.UtcNow - pairedAt);
                    return new PairingResult(true, "RecentlyPaired");
                }

                if (isPaired)
                {
                    var unpair = await existing!.DeviceInformation.Pairing.UnpairAsync().AsTask(cancellationToken);
                    logger.LogInformation("Unpaired stale bond for {Address}: {Status}", address, unpair.Status);
                    await Task.Delay(SettleAfterUnpair, cancellationToken);
                }
            }

            var addressType = isPublicAddress switch
            {
                true => BluetoothAddressType.Public,
                false => BluetoothAddressType.Random,
                null => BluetoothAddressType.Unspecified,
            };
            return Complete(address, await PairCoreAsync(
                () => BluetoothLEDevice.FromBluetoothAddressAsync(address.Value, addressType).AsTask(cancellationToken),
                address,
                cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Pairs a device found by the pairing window's device watcher.</summary>
    public async Task<PairingResult> PairAsync(
        DeviceInformation deviceInformation, BluetoothAddress address, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var suspended = maintainConnection.Suspend(address);
            return Complete(address, await PairCoreAsync(
                () => BluetoothLEDevice.FromIdAsync(deviceInformation.Id).AsTask(cancellationToken),
                address,
                cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Requests pairing every few seconds until it succeeds or times out. Mirrors the bare probe that
    /// paired the controller (2026-10-21): no MaintainConnection session (the caller suspends ours),
    /// no advertisement watcher running, and a <em>fresh</em> device object for every request — the
    /// object obtained right after unpairing may still carry the old bond.
    /// </summary>
    private async Task<PairingResult> PairCoreAsync(
        Func<Task<BluetoothLEDevice?>> openDevice, BluetoothAddress address, CancellationToken cancellationToken)
    {
        using var pause = advertisements.Pause();
        var started = DateTimeOffset.UtcNow;
        for (var attempt = 1; ; attempt++)
        {
            PairingResult result;
            using (var device = await openDevice())
            {
                if (device is null)
                {
                    result = new PairingResult(false, "DeviceNotFound");
                }
                else if (device.DeviceInformation.Pairing.IsPaired)
                {
                    return new PairingResult(true, nameof(DevicePairingResultStatus.AlreadyPaired));
                }
                else
                {
                    if (attempt == 1)
                    {
                        logger.LogInformation(
                            "Pairing {Address}: id={Id}, canPair={CanPair}, link={Link}",
                            address, device.DeviceId, device.DeviceInformation.Pairing.CanPair, device.ConnectionStatus);
                    }

                    // Only custom pairing: the basic DeviceInformationPairing.PairAsync does not work
                    // outside UWP (https://stackoverflow.com/questions/45191412).
                    result = await PairCustomAsync(device.DeviceInformation.Pairing.Custom, address, attempt, cancellationToken);
                }
            }

            if (result.Succeeded)
            {
                return result;
            }

            if (DateTimeOffset.UtcNow - started >= PairingTimeout)
            {
                logger.LogWarning("Gave up pairing {Address} after {Timeout}", address, PairingTimeout);
                return result;
            }

            await Task.Delay(RetryInterval, cancellationToken);
        }
    }

    private async Task<PairingResult> PairCustomAsync(
        DeviceInformationCustomPairing custom, BluetoothAddress address, int attempt, CancellationToken cancellationToken)
    {
        // Xbox controllers use "Just Works". ConfirmPinMatch is accepted too: a gamepad cannot show
        // a PIN, so Windows may offer it as an equivalent ceremony. Kinds and protection level follow
        // PolarGoose/BluetoothDevicePairing, a desktop tool known to pair BLE devices.
        TypedEventHandler<DeviceInformationCustomPairing, DevicePairingRequestedEventArgs> accept = (_, args) =>
        {
            logger.LogInformation("Pairing request from {Address}: {Kind}; accepting", address, args.PairingKind);
            args.Accept();
        };
        custom.PairingRequested += accept;
        try
        {
            var result = await custom
                .PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.ConfirmPinMatch, DevicePairingProtectionLevel.None)
                .AsTask(cancellationToken);
            logger.LogInformation(
                "Custom pairing of {Address} (attempt {Attempt}): {Status}, protection used={Protection}",
                address, attempt, result.Status, result.ProtectionLevelUsed);
            return ToResult(result);
        }
        finally
        {
            custom.PairingRequested -= accept;
        }
    }

    private static PairingResult ToResult(DevicePairingResult result) => new(
        result.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired,
        result.Status.ToString());

    private PairingResult Complete(BluetoothAddress address, PairingResult result)
    {
        if (result.Succeeded)
        {
            _pairedAt[address] = DateTimeOffset.UtcNow;
            logger.LogInformation("Paired {Address}: {Status}", address, result.Status);
            Paired?.Invoke(address);
        }
        else
        {
            logger.LogWarning("Pairing {Address} failed: {Status}", address, result.Status);
        }

        return result;
    }
}
