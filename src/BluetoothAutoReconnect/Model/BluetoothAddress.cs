using System.Globalization;

namespace BluetoothAutoReconnect.Model;

/// <summary>
/// 48-bit Bluetooth device address. Managed devices are identified by address, never by
/// friendly name ("Xbox Wireless Controller" is ambiguous on the dev machine).
/// </summary>
public readonly record struct BluetoothAddress(ulong Value)
{
    /// <summary>Accepts <c>123456789abc</c>, <c>12:34:56:78:9A:BC</c> or <c>12-34-56-78-9A-BC</c>.</summary>
    public static bool TryParse(string? text, out BluetoothAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim().Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (hex.Length != 12
            || !ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        address = new BluetoothAddress(value);
        return true;
    }

    public static BluetoothAddress Parse(string text) =>
        TryParse(text, out var address) ? address : throw new FormatException($"Invalid Bluetooth address: '{text}'.");

    /// <summary>Uppercase, no separators — the form used in <c>BTHLE\DEV_123456789ABC</c>.</summary>
    public string ToCompactString() => Value.ToString("X12", CultureInfo.InvariantCulture);

    public override string ToString()
    {
        var compact = ToCompactString();
        return string.Join(':', Enumerable.Range(0, 6).Select(i => compact.Substring(i * 2, 2)));
    }
}
