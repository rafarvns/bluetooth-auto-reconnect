namespace BluetoothAutoReconnect.Host.Updates;

public sealed class UpdateOptions
{
    public const string SectionName = "Updates";

    public bool Enabled { get; set; } = true;

    /// <summary>GitHub repository the releases come from, as <c>owner/name</c>.</summary>
    public string Repository { get; set; } = "rafarvns/bluetooth-auto-reconnect";

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);
}
