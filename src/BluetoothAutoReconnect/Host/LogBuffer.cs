using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace BluetoothAutoReconnect.Host;

/// <summary>
/// Serilog sink keeping the most recent log lines in memory for the live log window. Thread-safe.
/// </summary>
public sealed class LogBuffer(int capacity = 2000) : ILogEventSink
{
    private readonly MessageTemplateTextFormatter _formatter =
        new("{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

    private readonly Queue<string> _lines = new();
    private readonly Lock _lock = new();

    /// <summary>Raised on the logging thread with the formatted line (ends in a newline).</summary>
    public event Action<string>? LineAdded;

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAutoReconnect",
        "logs");

    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock)
        {
            return [.. _lines];
        }
    }

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);
        var line = writer.ToString();

        lock (_lock)
        {
            _lines.Enqueue(line);
            while (_lines.Count > capacity)
            {
                _lines.Dequeue();
            }
        }

        LineAdded?.Invoke(line);
    }
}
