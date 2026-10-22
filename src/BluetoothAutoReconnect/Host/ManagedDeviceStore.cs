using System.Text.Json;

using BluetoothAutoReconnect.Model;

using Microsoft.Extensions.Logging;

namespace BluetoothAutoReconnect.Host;

/// <summary>
/// Source of truth for which devices the agent manages. Seeded from <c>Agent:ManagedDevices</c>
/// until the first change, then persisted to a user-writable JSON file (the exe directory may not be).
/// Thread-safe.
/// </summary>
public sealed class ManagedDeviceStore
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly List<BluetoothAddress> _addresses;

    public ManagedDeviceStore(IEnumerable<string> seed, string filePath, ILogger<ManagedDeviceStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        _addresses = Parse(File.Exists(filePath) ? Load(filePath) : seed);
    }

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BluetoothAutoReconnect",
        "devices.json");

    public event Action? Changed;

    public IReadOnlyList<BluetoothAddress> Addresses
    {
        get
        {
            lock (_lock)
            {
                return [.. _addresses];
            }
        }
    }

    public bool Contains(BluetoothAddress address)
    {
        lock (_lock)
        {
            return _addresses.Contains(address);
        }
    }

    public bool Add(BluetoothAddress address) => Mutate(list =>
    {
        if (list.Contains(address))
        {
            return false;
        }

        list.Add(address);
        return true;
    });

    public bool Remove(BluetoothAddress address) => Mutate(list => list.Remove(address));

    private bool Mutate(Func<List<BluetoothAddress>, bool> change)
    {
        lock (_lock)
        {
            if (!change(_addresses))
            {
                return false;
            }

            Save();
        }

        Changed?.Invoke();
        return true;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(_addresses.Select(a => a.ToString()));
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _filePath, overwrite: true);
    }

    private IEnumerable<string> Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogError(ex, "Failed to read managed devices from {Path}", path);
            return [];
        }
    }

    private List<BluetoothAddress> Parse(IEnumerable<string> texts)
    {
        var result = new List<BluetoothAddress>();
        foreach (var text in texts)
        {
            if (!BluetoothAddress.TryParse(text, out var address))
            {
                _logger.LogError("Ignoring invalid Bluetooth address: {Address}", text);
            }
            else if (!result.Contains(address))
            {
                result.Add(address);
            }
        }

        return result;
    }
}
