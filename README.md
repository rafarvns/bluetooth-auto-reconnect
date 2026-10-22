# Bluetooth Auto Reconnect

A small Windows tray app that keeps a Bluetooth game controller usable without trips to
**Settings → Bluetooth & devices**. Built for, and tested with, the **Xbox Wireless Controller**
(Series X|S generation, which connects over Bluetooth Low Energy).

## The problem

If you use your Xbox controller with more than one device (a console, a phone, another PC), the
controller only remembers **one Bluetooth host at a time**. When you come back to your PC, the
pairing Windows has stored is no longer valid, and the controller just blinks forever. The usual
fix is tedious: open Settings, remove the controller, put it in pairing mode, add it again, every
single time.

## What this app does

- **One-step re-pairing.** When the controller won't connect, hold its **pair button** (the small
  button on top) until the Xbox button blinks fast. The app notices, removes the stale pairing and
  pairs again on its own, usually within about 10 seconds. You don't open any window.
- **Pairing window.** For the first pairing, or if the controller isn't paired at all, double-click
  the tray icon, put the controller in pairing mode, select it and click **Parear** (Pair).
- **Battery at a glance.** The tray icon is a gamepad colored by battery level:

  | Color | Battery |
  |---|---|
  | Green | above 60% |
  | Yellow | 30–60% |
  | Orange | 10–30% |
  | Red | below 10% |
  | Gray | disconnected |

  Hover the icon for the exact percentage.
- **Starts with Windows**, quietly, in the notification area.

It never pairs a controller that Windows has no pairing for on its own (that is left to you), so it
won't get in the way if you prefer pairing from Windows Settings.

## Install

1. Download `BluetoothAutoReconnect-Setup-<version>.exe` from the
   [Releases](../../releases) page.
2. Run it. It installs for your user only, so no administrator prompt. Windows SmartScreen may warn
   you because the installer isn't code-signed: click **More info → Run anyway**.
3. Leave **"Start automatically when I sign in to Windows"** checked.

**Make the icon always visible:** Windows hides new tray icons. Drag the icon from the `^` overflow
onto the taskbar, or go to **Settings → Personalization → Taskbar → Other system tray icons** and turn
on **Bluetooth Auto Reconnect**.

To uninstall, use **Settings → Apps → Installed apps → Bluetooth Auto Reconnect**. Your list of
managed controllers and the logs stay in `%LOCALAPPDATA%\BluetoothAutoReconnect`; delete that
folder if you want them gone too.

## Requirements

- Windows 10 version 2004 or later, or Windows 11 (x64)
- A Bluetooth adapter with Bluetooth Low Energy support
- No .NET install needed: the app is self-contained

## Usage

| Situation | What to do |
|---|---|
| First time | Double-click the tray icon, hold the controller's pair button until it blinks fast, select it under **Dispositivos em modo de pareamento**, click **Parear**. |
| Controller blinks and won't connect | Hold the pair button until it blinks fast. Wait ~10 s. |
| Stop managing a controller | Open the window, select it under **Controles gerenciados**, click **Parar de gerenciar**. |

The app's UI is in Portuguese; the labels above are what you will see.

## Known limitation

On some PCs, Windows never re-opens the Bluetooth LE link when the controller is switched back on
with just the Xbox button, even with a valid pairing. The app tries the documented approaches
(`GattSession.MaintainConnection`, connection attempts on advertisement), but if Windows refuses,
holding the pair button (one-step re-pairing) is the reliable path. Details and measurements are in
[`docs/manual-tests.md`](docs/manual-tests.md).

## Troubleshooting

Logs are written to `%LOCALAPPDATA%\BluetoothAutoReconnect\logs\` (one file per day, 14 days kept).
Look for lines like `Pairing request`, `Paired`, `connected via repair` or `Pairing ... failed`.

## Building from source

Requires the .NET 9 SDK, and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

```bash
dotnet build                                       # dev build (Debug)
dotnet test
dotnet run --project src/BluetoothAutoReconnect    # run the dev build
pwsh ./build-release.ps1                           # production exe + installer in artifacts/
```

There are two build profiles:

- **Debug (dev):** adds a live **Ver log…** (View log) window to the tray menu.
- **Release (production):** no log window; logs still go to the files above. This is what the
  installer ships.

## How it works (short version)

The app watches Bluetooth LE advertisements from the controllers it manages. When a managed
controller that Windows still has paired shows up in pairing mode and doesn't connect within a few
seconds, the app treats the stored pairing as stale: it unpairs, then pairs again through
`DeviceInformationCustomPairing`.

One non-obvious detail made this work: a held `GattSession` with `MaintainConnection` for the same
device makes every pairing request fail instantly, so the app releases its own session while
pairing. See [`CLAUDE.md`](CLAUDE.md) for the full design notes.

## License

[MIT](LICENSE)
