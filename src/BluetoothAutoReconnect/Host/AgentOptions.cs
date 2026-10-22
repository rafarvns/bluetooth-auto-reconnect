namespace BluetoothAutoReconnect.Host;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Addresses of the paired devices the agent manages, e.g. <c>12:34:56:78:9A:BC</c>.</summary>
    public List<string> ManagedDevices { get; set; } = [];

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
