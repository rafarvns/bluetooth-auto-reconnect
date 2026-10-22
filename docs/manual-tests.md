# Manual reconnect tests

Reconnect behaviour can only be validated against the physical controller. Record every run here —
this table is the evidence used to order the escalation chain.

Logs: `%LOCALAPPDATA%\BluetoothAutoReconnect\logs\`. For faster polling while testing, set
`Agent__PollInterval=00:00:05` in the environment. Start the agent with
`dotnet run --project src/BluetoothAutoReconnect`.

## Procedures

**A. Advertisement diagnostic** (do this first — the auto re-pair depends on it)
1. Agent running, controller off.
2. Turn the controller on normally → note the `Advertisement from …` line (kind, `discoverable`, flags).
3. Turn it off, then hold the pair button until the Xbox button blinks fast → note the line again.
4. Expected: `discoverable=True` only in step 3. If both are `True` or neither appears, the
   re-pair trigger in `AdvertisementReactor` needs a different signal.

**B. First pairing from the window**
1. Remove the controller in Windows Settings.
2. Tray → **Parear controle…**, put the controller in pairing mode, select it, **Parear**.
3. Pass = status says paired, managed list shows `Conectado — NN%`, gamepad works in `joy.cpl`.

**C. Automatic re-pair after using another host**
1. Use the controller with the other device, then come back to the PC.
2. Hold the pair button. Do **not** open Settings or the window.
3. Pass = notification "Controle pareado", log `reconnected after … via repair`, gamepad works.

**D. Power cycle using only this PC**
1. Controller connected. Turn it off (hold the Xbox button), wait, turn it on normally.
2. Pass = log `reconnected after … via maintain-connection` or `via advertisement-connect`,
   without pressing the pair button.

## Results

| Date | Commit | Procedure | Off for | Time to reconnect | Strategy | Result | Notes |
|------|--------|-----------|---------|-------------------|----------|--------|-------|
| 2026-10-21 | (uncommitted) | Agent start, controller off | — | — | maintain-connection | n/a | Session created with status `Closed`, `MaintainConnection=true`; not recreated on later polls. |
| 2026-10-21 | (uncommitted) | Power on after use (strategy 1 only) | ? | — | maintain-connection | **Fail** | No connection or session event at all when the controller powered on. The controller is also used with another host, so the bond was likely stale. |
| 2026-10-21 | (uncommitted) | Pairing from agent (auto + window) | — | — | repair | **Fail (invalid)** | Every attempt overlapped with the user pairing from Settings: custom pairing `Failed` in ~200 ms with no ceremony, basic `Failed`/`OperationAlreadyInProgress`. Agent then changed to never pair unpaired devices automatically. |
| 2026-10-21 | (uncommitted) | Auto re-pair, no Settings | — | — | repair | **Fail** | Detected pairing mode, unpaired stale bond, then `Custom.PairAsync` `Failed` in ~110 ms. Window pairing (active-scan AEP) failed the same way. |
| 2026-10-21 | (uncommitted) | Pair over an already-open link (probe app) | — | — | repair | **Pass** | Controller connected-but-unpaired via held GATT session; `Custom.PairAsync` raised `ConfirmOnly`, returned `Paired`, all PnP nodes OK, battery read. Led to connect-then-pair in `PairingService`. |
| 2026-10-21 | (uncommitted) | Bare probe pairing, agent closed (no session) | — | ~1 s after pair button | repair | **Pass** | Attempts took ~8 s each while not in pairing mode; first attempt after holding pair button: `ConfirmOnly` → `Paired`, gamepad OK. Proved the agent's own `MaintainConnection` session blocked pairing. |
| 2026-10-21 | (uncommitted) | Window pairing, agent mirrors probe | — | 1.3 s | repair | **Pass** | Session suspended, watcher paused, fresh device object per request: attempt 1 → `ConfirmOnly` → `Paired`; session re-held afterwards. First end-to-end pairing by the agent. |
| 2026-10-21 | (uncommitted) | C. Automatic re-pair (hold pair button, nothing else) | ~25 s | ~10 s from pair button | repair | **Pass** | Detected pairing mode, 5 s grace, unpaired stale bond, `Custom.PairAsync` attempt 1 → `Paired`, logged `connected via repair`, session re-held. The target flow works end to end. |
