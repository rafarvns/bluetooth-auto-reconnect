# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Windows tray agent that auto-reconnects **already-paired** Bluetooth devices when they power on —
primarily an Xbox Series X|S controller.

The problem being solved: after the controller is switched off, Windows does not re-establish the
link when it is switched back on. The user's current manual workaround is to remove the device in
Settings > Bluetooth and pair again, every time. The goal is: **turn the controller on, it works —
no user interaction, no re-pairing**.

It lives in the notification area (system tray) alongside Discord/Steam, and its icon is a gamepad
glyph whose color reflects the controller's battery level.

**Pairing is in scope (changed 2026-10-21 at the user's request).** The user switches the
controller between this PC and another host, and the controller keeps one Bluetooth host bond at a
time — so after using it elsewhere, Windows' bond is stale and *no* reconnect strategy can work.
The achievable flow is: first pairing from a window in the tray, and afterwards **hold the pair
button → the agent detects pairing mode, drops the stale bond and re-pairs by itself**. Re-pairing
needs the controller in pairing mode, which the user confirmed they already do every time.

**The agent must never compete with Windows Settings.** It only auto re-pairs when Windows still
holds a (stale) bond; a device that is not paired at all is left to the user (Settings or the
pairing window). An earlier version paired unpaired devices automatically, retrying every 60s, and
it collided with the user pairing from Settings — both sides failed (`Failed` in ~200 ms, no
pairing ceremony; `OperationAlreadyInProgress`).

## Status

What exists (2026-10-21):

- Solution, `Directory.Build.props` (shared TFM `net9.0-windows10.0.22621.0`, warnings as errors,
  code style enforced in build), `global.json` pinning SDK 9.0.x.
- Generic Host + Serilog (console + rolling file in `%LOCALAPPDATA%\BluetoothAutoReconnect\logs`),
  config in `appsettings.json` (`Agent:ManagedDevices` seed, `Agent:PollInterval`).
- `ManagedDeviceStore`: managed addresses, persisted to
  `%LOCALAPPDATA%\BluetoothAutoReconnect\devices.json` after the first change (seed ignored then).
- `WinRtDeviceSource` reading connection state and battery. The PnP battery query (AQS
  `System.Devices.DeviceInstanceId:~<"BTHLE\DEV_<addr>"`) is **verified working** on the
  controller — returned `76` while disconnected (the value is cached by Windows).
- `AdvertisementMonitor`: passive LE watcher filtered to managed addresses; logs advert type and
  flags (diagnostic) and feeds `AdvertisementReactor` (pure, tested), which decides
  ignore / connect attempt (strategy 2, with backoff) / re-pair (only paired + disconnected +
  discoverable, after a 5s grace, at most once per pairing-mode session).
- `PairingService`: unpair stale bond + custom "Just Works" pairing; shared by the window and the
  automatic re-pair. `PairingForm` (tray menu "Parear controle…" or double-click the icon).
- Tray icon: `GamepadIconRenderer` draws a gamepad filled with the `BatteryPalette` color plus a dark
  outline (checked on light and dark backgrounds at 16/24/32 px); `TrayIconSet` caches one icon per
  bucket at `SystemInformation.SmallIconSize`, cloning and `DestroyIcon`-ing the temporary HICON so
  nothing leaks. Not re-rendered on a live DPI change (restart picks up the new size).
- `LogForm` (tray menu "Ver log…") shows the log live via the in-memory `LogBuffer` Serilog sink.
- `DeviceMonitor` ties it together and logs `reconnected after … via <strategy>`.
- Pure, tested: `BluetoothAddress`, `BatteryPalette`, `BackoffPolicy`, `ConnectionTransitions`,
  `AdvertisementReactor`, `ManagedDeviceStore`.
- Strategies 1 (`MaintainConnectionStrategy`), 2 (`AdvertisementConnectStrategy`) and the re-pair
  path are implemented; device-node restart (3) is not.

Measured results (details in `docs/manual-tests.md`):

- **Strategy 1 alone did not reconnect.** Powering the controller on produced *no*
  `ConnectionStatusChanged` / `SessionStatusChanged` event at all — Windows never saw a link
  attempt. Consistent with a stale bond after using another host.
- With the controller off, `GattSession.FromDeviceIdAsync` succeeds and reports `Closed`; that is
  the normal "device off" state, so the session is held regardless of status.
- **Verified:** the controller advertises `ConnectableUndirected` from a **Public** address. Flags
  are `GeneralDiscoverableMode, ClassicNotSupported` in pairing mode (Xbox button blinking fast)
  and only `ClassicNotSupported` when simply powered on. It alternates with `ScanResponse` packets
  that carry no flags — ignore those.
- Radio: CSR8510 USB dongle (`USB\VID_0A12&PID_0001`, "Generic Bluetooth Radio", Microsoft
  driver). The user uses it daily without problems; do not blame it without evidence.
- **Power cycle with a fresh, valid bond (paired from Settings) also did not reconnect.** On power-on
  the controller advertises `ConnectableUndirected`, non-discoverable — i.e. it *is* trying to
  reconnect — yet `GetGattServicesAsync(Uncached)` returns `Unreachable` in ~200 ms and Windows
  never opens the link. Neither `pnputil /restart-device` on the `BTHLE\DEV_` node nor toggling the
  Bluetooth radio, both done while the controller was advertising, made it connect. So strategies
  1–3 are all ineffective on this machine; only pairing works.
- Radio is LMP 6 (Bluetooth 4.0), yet `BluetoothAdapter.AreLowEnergySecureConnectionsSupported`
  reports `True`. Prior art with the same symptom ("only the sync button brings it back"):
  ndeadly/MissionControl PR #1148 — there the cause was a host without LE Secure Connections.
  Not confirmed here.
- Desktop pairing: `DeviceInformationPairing.PairAsync` (basic) does not work outside UWP; use
  `Custom.PairAsync` only. Kinds/protection now follow PolarGoose/BluetoothDevicePairing
  (`ConfirmOnly | ConfirmPinMatch`, `ProtectionLevel.None`).
- **Root cause of the pairing failures: our own `MaintainConnection` session.** While the agent held
  a `GattSession` with `MaintainConnection = true` for the controller, every `Custom.PairAsync`
  failed in ~100 ms with no `PairingRequested` — Windows did not even try to connect. With **no
  session held anywhere** (agent closed, a bare probe calling only `Custom.PairAsync` every 3 s), each
  request took ~8 s to time out while the controller was not in pairing mode, and the first request
  after the user held the pair button raised `ConfirmOnly` and returned `Paired` (verified
  2026-10-21 21:01; gamepad worked). The single earlier success also happened over a link that was
  already open. Likely mechanism (unproven): the session keeps a background LE connection pending,
  and this Bluetooth 4.0 radio allows only one pending LE connection, so the pairing connection is
  refused. Settings worked because nothing held a session while the user paired there.
  **Rule: never hold a `MaintainConnection` session for a device while pairing it.**
  `PairingService` calls `MaintainConnectionStrategy.Suspend(address)` before pairing and retries
  `Custom.PairAsync` every 3 s for up to 90 s. Suspending alone was **not** enough (still ~100 ms
  failures); it worked once pairing also mirrored the probe: advertisement watcher paused for the
  whole pairing, the window's active scan stopped, and a **fresh `BluetoothLEDevice` per request**
  (verified 21:21, paired on attempt 1 in 1.3 s). Which of those three matters is not isolated —
  keep all of them. Dead ends measured on the way: GATT-discovery spam,
  passive waiting with a session, pairing retries with a session.
- Earlier note, now superseded: pairing from the agent had not succeeded; every attempt so far overlapped with the user
  pairing from Settings, so those failures prove nothing about the API calls themselves.

Everything else below is still intended design. Update this file as reality diverges.

## Verified hardware facts

Measured on the dev machine (Windows 11) with the controller connected. These were
checked against the real device, not assumed — **do not re-derive them from guesswork, and do not
"correct" them back to the common assumptions, which are wrong here.**

- **The Xbox Wireless Controller connects over Bluetooth *Low Energy*, not Bluetooth Classic.**
  It enumerates as `BTHLE\DEV_<ADDRESS>`, VID `045E` PID `0B13`.
  Most community guidance about Xbox controllers says BR/EDR; for this device it is wrong.
- It uses **HID over GATT (HOGP, service `0x1812`)** — the gamepad appears at
  `HID\{00001812-0000-1000-8000-00805F9B34FB}&DEV&VID_045E&PID_0B13&...&IG_00`.
- It exposes the **GATT Battery Service `0x180F`** and Device Information Service `0x180A`.
- **Battery is readable from the PnP device property** `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`
  (DEVPKEY_Bluetooth_Battery) on the `BTHLE\DEV_...` node, as a `Byte` percentage. Confirmed
  returning `76`.
- The same node exposes `DEVPKEY_Bluetooth_LastConnectedTime` and `DEVPKEY_Bluetooth_DeviceAddress`
  (the 12-hex-digit address), both useful for reconnect logic and for identifying the managed device.
- The machine also has ViGEm (`Nefarius Virtual Gamepad Emulation Bus`), Xbox 360 controllers, and
  a USB/dongle Xbox controller (`USB\VID_045E&PID_0B12`) present. **Filter by Bluetooth address, not
  by friendly name** — "Xbox Wireless Controller" is ambiguous on this machine.

Re-run these to re-verify after a Windows update:

```bash
pwsh -NoProfile -c "Get-PnpDevice -Class Bluetooth | Format-Table Status,FriendlyName,InstanceId"
pwsh -NoProfile -c "Get-PnpDeviceProperty -InstanceId 'BTHLE\DEV_<ADDRESS>\<INSTANCE>' -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2' | Format-List"
```

## Stack and run model (decided)

- **.NET 9 / C#**, for first-class WinRT access (`Windows.Devices.Bluetooth`,
  `Windows.Devices.Enumeration`) plus single-file publish.
- **User-session tray app started at logon**, *not* a Windows Service. Deliberate: WinRT Bluetooth
  and GATT are unreliable or blocked in session 0 (SYSTEM). Consequence: it does not run before
  logon, which is accepted.
- TFM must be SDK-versioned — `net9.0-windows10.0.22621.0` or similar. Plain `net9.0` will not
  resolve `Windows.Devices.*`. Tray UI needs `<UseWindowsForms>true</UseWindowsForms>`.

## Commands

Layout: `src/BluetoothAutoReconnect/`, `tests/BluetoothAutoReconnect.Tests/`, `installer/`
(Inno Setup script), `assets/app.ico` (generated from `GamepadIconRenderer`).

```bash
dotnet build                                            # Debug = dev profile
dotnet run --project src/BluetoothAutoReconnect         # run the dev build
dotnet test
dotnet test --filter "FullyQualifiedName~BatteryPaletteTests.MapsLowBatteryToRed"   # single test
dotnet format
pwsh ./build-release.ps1        # Release exe (single-file, self-contained) + installer -> artifacts/
```

**Build profiles.** Debug defines `LOG_WINDOW`: the tray menu gets "Ver log…" (`LogForm`, fed by the
in-memory `LogBuffer` Serilog sink). Release does not compile `LogForm.cs`/`LogBuffer.cs` at all;
rolling log files are written in both. Guard any dev-only UI with `#if LOG_WINDOW`.

**Installer / auto-start.** `installer/BluetoothAutoReconnect.iss` installs per user
(`{localappdata}\Programs\Bluetooth Auto Reconnect`, no admin) and starts the agent at sign-in via
the `HKCU\...\CurrentVersion\Run` value `BluetoothAutoReconnect` (optional task, on by default).
Not a Windows Service, and no elevated scheduled task: pairing works unelevated, and a service
(session 0) has no tray and breaks WinRT pairing. Uninstall kills the process and removes the Run
value; user data in `%LOCALAPPDATA%\BluetoothAutoReconnect` (devices.json, logs) is kept.
The version comes from `<Version>` in the app `.csproj`. The installer is not code-signed.

**Privacy.** The repo is public. Never commit real device addresses, PnP instance IDs or other
machine identifiers — use `12:34:56:78:9A:BC` / `<ADDRESS>` placeholders. The default
`Agent:ManagedDevices` is empty; real devices live only in the user's `devices.json`.

Hardware debugging:

```bash
pnputil /enum-devices /connected /class Bluetooth
pnputil /restart-device "BTHLE\DEV_<ADDRESS>\<INSTANCE>"   # heavy fallback
```

## Architecture

Five layers; the only hard-to-test seam is interop.

1. **Device access** (`Devices/`) — WinRT `BluetoothLEDevice`, `GattSession`,
   `BluetoothLEAdvertisementWatcher`, plus PnP property reads (`DeviceInformation` /
   `Windows.Devices.Enumeration.Pnp.PnpObject`) for battery and last-connected-time.
2. **Device model** — `Address`, `Name`, `IsPaired`, `IsConnected`, `BatteryPercent`,
   `LastConnected`. Everything downstream consumes this, never raw WinRT types.
3. **Reconnect strategy** — ordered escalation chain (below), each step behind one interface so it
   can be reordered, disabled per device, and unit-tested.
4. **Tray UI** (`Tray/`) — `NotifyIcon`, battery-colored icon, context menu, tooltip.
5. **Host** — config (which addresses are managed), structured rolling-file logging, backoff policy.

Keep all decisions (when to attempt, which strategy next, backoff, give-up, which color) in pure
classes taking the device model as input. Those are the tests worth writing; interop is verified by
hand against the controller.

## Reconnect strategies, cheapest first

Because the device is **BLE**, this is very different from the Classic approach most guidance
describes. Try the least disruptive thing first, escalate only on failure:

1. **`GattSession.MaintainConnection = true`** — get a `GattSession` via
   `GattSession.FromDeviceIdAsync(BluetoothDeviceId.FromId(deviceId))` and set
   `MaintainConnection`. This tells Windows to hold the link and re-establish it whenever the
   device advertises again. This is the most likely single fix for the whole problem; hold the
   session for the agent's lifetime rather than connecting on demand.
2. **Advertisement-triggered connect** — `BluetoothLEAdvertisementWatcher` filtered to the managed
   address; on advertisement, `BluetoothLEDevice.FromBluetoothAddressAsync()` and then
   `GetGattServicesAsync(Uncached)` to force an actual connection attempt. Note that simply
   obtaining a `BluetoothLEDevice` does *not* connect — a GATT operation is what drives the link.
3. **Device-node restart** — `pnputil /restart-device` or SetupAPI/cfgmgr32 remove + rescan on the
   `BTHLE\DEV_...` node.
4. **Unpair + re-pair** — `DeviceInformationPairing.UnpairAsync` then pair. Last resort; it mirrors
   the manual workaround and needs the controller in pairing mode, so it generally cannot run
   unattended. Prefer a tray notification telling the user to do it.

Always re-read device state after an attempt instead of assuming success, and log which strategy
worked — that history is what tunes the order.

## Tray icon

The user explicitly wants it to sit in the notification area like Discord/Steam, with a gamepad
icon colored by battery.

- **Color buckets** (keep in one pure, tested mapping function): healthy green > 60, yellow 30–60,
  orange 10–30, red < 10, plus a distinct **disconnected/unknown** state. Battery is a `Byte` 0–100.
- **Only redraw when the bucket changes**, not on every poll — see the handle leak below.
- **`Icon.FromHandle` leaks GDI handles.** When generating tinted icons at runtime, call
  `DestroyIcon` on the old `HICON` after swapping, or the agent bleeds handles over a long session.
  Alternatively pre-render one `.ico` per bucket and ship them as resources — simpler and leak-free.
- **Provide 16/20/24/32 px variants** so the icon stays sharp across DPI settings.
- **Must be legible on both light and dark taskbars.** A tinted glyph with no contrasting outline
  disappears on one of them; the gray "disconnected" state is the worst offender.
- **Windows hides new tray icons by default.** The user will have to promote it to always-visible
  once. Mention this in the README, since "visible like Discord" is an explicit requirement and a
  hidden icon reads as a bug.
- Re-add the icon on the `TaskbarCreated` message so it survives an Explorer restart. Verify
  whether WinForms `NotifyIcon` already handles this before writing custom code.
- Tooltip should carry the real status, e.g. `Xbox Wireless Controller — 76%`.

Battery source: prefer the verified PnP property above. The GATT `0x180F` / `0x2A19`
characteristic is the fallback, and is worth using only if it proves more responsive — reading it
requires an active connection, whereas the PnP property is cached by Windows.

## Other constraints

- **Be idempotent; check connection state before acting.** Running a reconnect strategy against a
  working link can drop it.
- **Back off.** Exponential backoff with a cap and a give-up state, reset on successful connect or
  on an advertisement from the device. A hot retry loop destabilizes the Bluetooth stack.
- **Do not run inquiry/discovery scans on a loop.** They hog the radio and disrupt other links. The
  LE advertisement watcher is passive and is the correct trigger here.
- **The controller holds one host bond at a time.** If it has since been paired to an Xbox console
  or a phone, Windows' bond is stale and no strategy can work — real re-pairing is required. Detect
  this and surface it in the tray instead of retrying forever.

## Testing

Unit tests cover strategy selection, backoff, state machine, and the battery→color mapping against
a faked device source. Interop and actual reconnect behaviour can only be validated against the
physical controller: power it off, wait, power it on, confirm it reconnects without touching
Settings. Record manual results in the repo as strategies are proven — that is the only evidence
available for ordering the escalation chain.
