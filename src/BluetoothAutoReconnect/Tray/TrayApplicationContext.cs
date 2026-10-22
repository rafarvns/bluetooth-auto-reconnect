using BluetoothAutoReconnect.Model;

namespace BluetoothAutoReconnect.Tray;

/// <summary>Owns the notification-area icon. Must be created and used on the UI thread.</summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int MaxTooltipLength = 127;

    private readonly NotifyIcon _notifyIcon;
    private readonly TrayIconSet _icons = new();
    private readonly SingleInstanceWindow _pairingWindow;
#if LOG_WINDOW
    private readonly SingleInstanceWindow _logWindow;
#endif
    private readonly ToolStripMenuItem _updateItem = new() { Visible = false };
    private Func<IProgress<int>, Task<bool>>? _installUpdate;
    private Version? _updateVersion;
    private BatteryBucket? _currentBucket;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        var pairItem = new ToolStripMenuItem("Parear controle…") { Font = new Font(menu.Font, FontStyle.Bold) };
        _pairingWindow = new SingleInstanceWindow(pairItem);
        menu.Items.Add(pairItem);
#if LOG_WINDOW
        // Dev builds only.
        var logItem = new ToolStripMenuItem("Ver log…");
        _logWindow = new SingleInstanceWindow(logItem);
        menu.Items.Add(logItem);
#endif
        // Hidden until a newer release exists; the user decides whether to click it.
        _updateItem.Click += OnUpdateClicked;
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icons.For(BatteryBucket.Disconnected),
            Text = "Bluetooth Auto Reconnect",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => _pairingWindow.Show();
    }

    /// <summary>Set once the host is built; the window needs its services.</summary>
    public void SetPairingWindowFactory(Func<Form> factory) => _pairingWindow.Factory = factory;

#if LOG_WINDOW
    public void SetLogWindowFactory(Func<Form> factory) => _logWindow.Factory = factory;
#endif

    public void SetUpdateHandler(Func<IProgress<int>, Task<bool>> installUpdate) => _installUpdate = installUpdate;

    /// <summary>Reveals the update menu item. Deliberately silent: no balloon, no prompt.</summary>
    public void ShowUpdateAvailable(Version version)
    {
        if (_updateVersion == version && _updateItem.Visible)
        {
            return;
        }

        _updateVersion = version;
        _updateItem.Text = $"Atualizar para a versão {version.ToString(3)}";
        _updateItem.Enabled = true;
        _updateItem.Visible = true;
    }

    private async void OnUpdateClicked(object? sender, EventArgs e)
    {
        if (_installUpdate is null)
        {
            return;
        }

        _updateItem.Enabled = false;
        _updateItem.Text = "Baixando atualização…";
        var progress = new Progress<int>(percent => _updateItem.Text = $"Baixando atualização… {percent}%");
        if (await _installUpdate(progress))
        {
            // The installer replaces the files and starts the new version.
            ExitThread();
            return;
        }

        _updateItem.Text = "Falha ao atualizar — tentar de novo";
        _updateItem.Enabled = true;
    }

    public void Update(BluetoothDeviceState? device)
    {
        var text = device switch
        {
            null => "Bluetooth Auto Reconnect — nenhum controle",
            { IsConnected: true, BatteryPercent: { } percent } => $"{device.Name} — {percent}%",
            { IsConnected: true } => $"{device.Name} — conectado",
            _ => $"{device.Name} — desconectado",
        };
        _notifyIcon.Text = text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;

        var bucket = BatteryPalette.BucketFor(device);
        if (bucket == _currentBucket)
        {
            return;
        }

        _currentBucket = bucket;
        _notifyIcon.Icon = _icons.For(bucket);
    }

    public void ShowNotification(string title, string text, ToolTipIcon icon) =>
        _notifyIcon.ShowBalloonTip(5000, title, text, icon);

    protected override void ExitThreadCore()
    {
        _pairingWindow.Close();
#if LOG_WINDOW
        _logWindow.Close();
#endif
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A window opened from a menu item; reopening brings the existing one to front.</summary>
    private sealed class SingleInstanceWindow
    {
        private readonly ToolStripMenuItem _menuItem;
        private Func<Form>? _factory;
        private Form? _current;

        public SingleInstanceWindow(ToolStripMenuItem menuItem)
        {
            _menuItem = menuItem;
            _menuItem.Enabled = false;
            _menuItem.Click += (_, _) => Show();
        }

        public Func<Form>? Factory
        {
            get => _factory;
            set
            {
                _factory = value;
                _menuItem.Enabled = value is not null;
            }
        }

        public void Show()
        {
            if (_current is { IsDisposed: false })
            {
                _current.WindowState = FormWindowState.Normal;
                _current.Activate();
                return;
            }

            if (Factory is null)
            {
                return;
            }

            _current = Factory();
            _current.FormClosed += (_, _) => _current = null;
            _current.Show();
            _current.Activate();
        }

        public void Close() => _current?.Close();
    }
}
