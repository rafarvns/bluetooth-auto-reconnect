using System.Diagnostics;

using BluetoothAutoReconnect.Host;

namespace BluetoothAutoReconnect.Tray;

/// <summary>Live view of the agent log. The full history stays in the rolling files.</summary>
internal sealed class LogForm : Form
{
    private const int MaxTextLength = 1_000_000;

    private readonly LogBuffer _buffer;
    private readonly TextBox _text = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f),
        BackColor = SystemColors.Window,
    };

    public LogForm(LogBuffer buffer)
    {
        _buffer = buffer;

        Text = "Log — Bluetooth Auto Reconnect";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 600);
        Padding = new Padding(8);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(NewButton("Abrir pasta", (_, _) => OpenFolder()));
        buttons.Controls.Add(NewButton("Copiar tudo", (_, _) => CopyAll()));
        buttons.Controls.Add(NewButton("Limpar", (_, _) => _text.Clear()));

        Controls.Add(_text);
        Controls.Add(buttons);

        _text.Text = string.Concat(_buffer.Snapshot());
        _buffer.LineAdded += OnLineAdded;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ScrollToEnd();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _buffer.LineAdded -= OnLineAdded;
        base.OnFormClosed(e);
    }

    private void OnLineAdded(string line)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (_text.TextLength > MaxTextLength)
                {
                    _text.Text = _text.Text[(MaxTextLength / 2)..];
                }

                _text.AppendText(line);
            });
        }
        catch (InvalidOperationException)
        {
            // Window closed between the check and the call.
        }
    }

    private void ScrollToEnd()
    {
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }

    private void CopyAll()
    {
        if (_text.TextLength > 0)
        {
            Clipboard.SetText(_text.Text);
        }
    }

    private static void OpenFolder()
    {
        System.IO.Directory.CreateDirectory(LogBuffer.Directory);
        Process.Start(new ProcessStartInfo(LogBuffer.Directory) { UseShellExecute = true });
    }

    private static Button NewButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        button.Click += onClick;
        return button;
    }
}
