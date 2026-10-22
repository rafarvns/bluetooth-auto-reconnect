using System.Runtime.InteropServices;

namespace BluetoothAutoReconnect.Tray;

/// <summary>
/// One gamepad icon per battery bucket, rendered once at the system small-icon size and reused.
/// Each icon is an owned copy: the temporary HICON from <see cref="Bitmap.GetHicon"/> is destroyed
/// right away, so swapping icons never leaks GDI handles.
/// </summary>
internal sealed class TrayIconSet : IDisposable
{
    private readonly Dictionary<BatteryBucket, Icon> _icons = [];
    private readonly int _size = SystemInformation.SmallIconSize.Width;

    public Icon For(BatteryBucket bucket)
    {
        if (!_icons.TryGetValue(bucket, out var icon))
        {
            icon = Create(BatteryPalette.ColorFor(bucket), _size);
            _icons[bucket] = icon;
        }

        return icon;
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }

    private static Icon Create(Color fill, int size)
    {
        using var bitmap = GamepadIconRenderer.Render(fill, size);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
