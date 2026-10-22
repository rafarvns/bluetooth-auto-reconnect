using BluetoothAutoReconnect.Devices;
using BluetoothAutoReconnect.Host;
using BluetoothAutoReconnect.Model;

using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BluetoothAutoReconnect.Tray;

/// <summary>
/// First-time pairing without Windows Settings, plus manual re-pair of managed devices.
/// The device watcher actively scans, so it only runs while this window is open.
/// </summary>
internal sealed class PairingForm : Form
{
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string SignalProperty = "System.Devices.Aep.SignalStrength";

    private readonly PairingService _pairing;
    private readonly ManagedDeviceStore _store;
    private readonly IDeviceSource _deviceSource;

    private readonly ListView _managedList = NewList(("Nome", 230), ("Endereço", 140), ("Status", 150));
    private readonly ListView _nearbyList = NewList(("Nome", 230), ("Endereço", 140), ("Sinal", 80));
    private readonly Button _repairButton = NewButton("Parear de novo");
    private readonly Button _forgetButton = NewButton("Parar de gerenciar");
    private readonly Button _pairButton = NewButton("Parear");
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };

    private readonly Dictionary<string, DeviceInformation> _nearby = [];
    private DeviceWatcher? _watcher;
    private bool _busy;
    private bool _refreshing;

    public PairingForm(PairingService pairing, ManagedDeviceStore store, IDeviceSource deviceSource)
    {
        _pairing = pairing;
        _store = store;
        _deviceSource = deviceSource;

        Text = "Parear controle — Bluetooth Auto Reconnect";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 580);
        MinimumSize = new Size(520, 460);
        Padding = new Padding(10);

        var instructions = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(600, 0),
            Text = "Para parear: segure o botão de parear do controle (o pequeno, no topo) até o botão Xbox "
                + "piscar rápido. Ele aparece em \"Dispositivos em modo de pareamento\" — selecione e clique em Parear.\n\n"
                + "Se um controle já pareado parou de conectar (por exemplo, depois de usá-lo em outro aparelho), "
                + "basta colocá-lo em modo de pareamento: o agente refaz o pareamento sozinho, mesmo com esta janela fechada. "
                + "Controles sem pareamento nenhum o agente não pareia sozinho — use esta janela ou o Windows.",
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(instructions);
        layout.Controls.Add(NewGroup("Controles gerenciados", _managedList, _repairButton, _forgetButton));
        layout.Controls.Add(NewGroup("Dispositivos em modo de pareamento", _nearbyList, _pairButton));
        layout.Controls.Add(_status);
        Controls.Add(layout);

        _managedList.SelectedIndexChanged += (_, _) => UpdateButtons();
        _nearbyList.SelectedIndexChanged += (_, _) => UpdateButtons();
        _nearbyList.DoubleClick += OnPairClicked;
        _pairButton.Click += OnPairClicked;
        _repairButton.Click += OnRepairClicked;
        _forgetButton.Click += OnForgetClicked;
        _refreshTimer.Tick += (_, _) => _ = RefreshManagedAsync();
        _store.Changed += OnStoreChanged;
        UpdateButtons();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _ = RefreshManagedAsync();
        _refreshTimer.Start();
        StartWatcher();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _store.Changed -= OnStoreChanged;
        _refreshTimer.Stop();
        StopWatcher();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void StartWatcher()
    {
        _watcher = DeviceInformation.CreateWatcher(
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(false),
            [AddressProperty, SignalProperty],
            DeviceInformationKind.AssociationEndpoint);

        _watcher.Added += (_, info) => OnUi(() =>
        {
            _nearby[info.Id] = info;
            RebuildNearbyList();
        });
        _watcher.Updated += (_, update) => OnUi(() =>
        {
            if (_nearby.TryGetValue(update.Id, out var info))
            {
                info.Update(update);
                RebuildNearbyList();
            }
        });
        _watcher.Removed += (_, update) => OnUi(() =>
        {
            if (_nearby.Remove(update.Id))
            {
                RebuildNearbyList();
            }
        });
        _watcher.Start();
    }

    private void StopWatcher()
    {
        if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
        {
            _watcher.Stop();
        }

        _watcher = null;
    }

    private void RebuildNearbyList()
    {
        var selectedId = (_nearbyList.SelectedItems.Count > 0 ? _nearbyList.SelectedItems[0].Tag as DeviceInformation : null)?.Id;
        var rows = _nearby.Values
            .Where(info => !string.IsNullOrWhiteSpace(info.Name) && TryGetAddress(info, out _))
            .OrderByDescending(info => info.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(info => Signal(info) ?? int.MinValue)
            .Select(info =>
            {
                TryGetAddress(info, out var address);
                var signal = Signal(info) is { } dbm ? $"{dbm} dBm" : "—";
                return new ListViewItem([info.Name, address.ToString(), signal]) { Tag = info, Selected = info.Id == selectedId };
            })
            .ToArray();

        _nearbyList.BeginUpdate();
        _nearbyList.Items.Clear();
        _nearbyList.Items.AddRange(rows);
        _nearbyList.EndUpdate();
        UpdateButtons();
    }

    private async Task RefreshManagedAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var rows = new List<ListViewItem>();
            foreach (var address in _store.Addresses)
            {
                BluetoothDeviceState? state = null;
                try
                {
                    state = await _deviceSource.GetStateAsync(address, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Shown as unknown; the agent log has the details.
                }

                rows.Add(new ListViewItem([state?.Name ?? "(desconhecido)", address.ToString(), Describe(state)]) { Tag = address });
            }

            if (IsDisposed)
            {
                return;
            }

            var selected = SelectedManagedAddress();
            foreach (var row in rows)
            {
                row.Selected = row.Tag is BluetoothAddress address && address == selected;
            }

            _managedList.BeginUpdate();
            _managedList.Items.Clear();
            _managedList.Items.AddRange([.. rows]);
            _managedList.EndUpdate();
            UpdateButtons();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async void OnPairClicked(object? sender, EventArgs e)
    {
        if (_busy || _nearbyList.SelectedItems.Count == 0
            || _nearbyList.SelectedItems[0].Tag is not DeviceInformation info
            || !TryGetAddress(info, out var address))
        {
            return;
        }

        // The agent's Paired handler adds the device to the managed list.
        await RunAsync($"Pareando {info.Name}… (pode levar até 1 minuto)", async () =>
        {
            // The active scan stays off while pairing: the request that worked ran with no scan at all.
            StopWatcher();
            PairingResult result;
            try
            {
                result = await _pairing.PairAsync(info, address, CancellationToken.None);
            }
            finally
            {
                if (!IsDisposed)
                {
                    StartWatcher();
                }
            }

            return result.Succeeded
                ? $"{info.Name} pareado. A partir de agora ele é reconectado automaticamente."
                : $"Falha ao parear ({result.Status}). Confira se o controle ainda está em modo de pareamento.";
        });
    }

    private async void OnRepairClicked(object? sender, EventArgs e)
    {
        if (_busy || SelectedManagedAddress() is not { } address)
        {
            return;
        }

        await RunAsync($"Pareando {address} de novo — o controle precisa estar em modo de pareamento…", async () =>
        {
            var result = await _pairing.RepairAsync(address, isPublicAddress: null, CancellationToken.None);
            return result.Succeeded
                ? $"{address} pareado de novo."
                : $"Falha ao parear ({result.Status}). Segure o botão de parear do controle e tente de novo.";
        });
    }

    private void OnForgetClicked(object? sender, EventArgs e)
    {
        if (!_busy && SelectedManagedAddress() is { } address)
        {
            _store.Remove(address);
            _status.Text = $"{address} não é mais gerenciado. O pareamento no Windows foi mantido.";
        }
    }

    private async Task RunAsync(string busyText, Func<Task<string>> operation)
    {
        _busy = true;
        UpdateButtons();
        _status.Text = busyText;
        try
        {
            _status.Text = await operation();
        }
        catch (Exception ex)
        {
            _status.Text = $"Erro: {ex.Message}";
        }
        finally
        {
            _busy = false;
            UpdateButtons();
            await RefreshManagedAsync();
        }
    }

    private void OnStoreChanged() => OnUi(() => _ = RefreshManagedAsync());

    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Window closed between the check and the call.
        }
    }

    private void UpdateButtons()
    {
        _pairButton.Enabled = !_busy && _nearbyList.SelectedItems.Count > 0;
        _repairButton.Enabled = _forgetButton.Enabled = !_busy && _managedList.SelectedItems.Count > 0;
    }

    private BluetoothAddress? SelectedManagedAddress() =>
        _managedList.SelectedItems.Count > 0 && _managedList.SelectedItems[0].Tag is BluetoothAddress address
            ? address
            : null;

    private static string Describe(BluetoothDeviceState? state) => state switch
    {
        null or { IsPaired: false } => "Não pareado",
        { IsConnected: true, BatteryPercent: { } percent } => $"Conectado — {percent}%",
        { IsConnected: true } => "Conectado",
        _ => "Desconectado",
    };

    private static bool TryGetAddress(DeviceInformation info, out BluetoothAddress address)
    {
        address = default;
        return info.Properties.TryGetValue(AddressProperty, out var value)
            && BluetoothAddress.TryParse(value as string, out address);
    }

    private static int? Signal(DeviceInformation info) =>
        info.Properties.TryGetValue(SignalProperty, out var value) && value is int dbm ? dbm : null;

    private static ListView NewList(params (string Title, int Width)[] columns)
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
        };
        foreach (var (title, width) in columns)
        {
            list.Columns.Add(title, width);
        }

        return list;
    }

    private static Button NewButton(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };

    private static GroupBox NewGroup(string title, ListView list, params Button[] buttons)
    {
        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
        };
        buttonRow.Controls.AddRange(buttons);

        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        inner.Controls.Add(list);
        inner.Controls.Add(buttonRow);

        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(inner);
        return group;
    }
}
