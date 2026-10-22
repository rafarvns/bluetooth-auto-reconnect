using BluetoothAutoReconnect.Host;
using BluetoothAutoReconnect.Model;

using Microsoft.Extensions.Logging.Abstractions;

namespace BluetoothAutoReconnect.Tests;

public sealed class ManagedDeviceStoreTests : IDisposable
{
    private static readonly BluetoothAddress Controller = BluetoothAddress.Parse("12:34:56:78:9A:BC");
    private static readonly BluetoothAddress Other = BluetoothAddress.Parse("AA:BB:CC:DD:EE:FF");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"bar-tests-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_directory, "devices.json");

    private ManagedDeviceStore NewStore(params string[] seed) =>
        new(seed, FilePath, NullLogger<ManagedDeviceStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void UsesSeedWhenNoFileExists() =>
        Assert.Equal([Controller], NewStore("123456789abc").Addresses);

    [Fact]
    public void IgnoresInvalidAndDuplicateSeedEntries() =>
        Assert.Equal([Controller], NewStore("123456789abc", "not-an-address", "12:34:56:78:9A:BC").Addresses);

    [Fact]
    public void PersistsChangesAndPrefersFileOverSeed()
    {
        var store = NewStore("123456789abc");
        store.Add(Other);
        store.Remove(Controller);

        var reloaded = NewStore("123456789abc");

        Assert.Equal([Other], reloaded.Addresses);
    }

    [Fact]
    public void AddIsIdempotentAndRaisesChangedOnlyOnChange()
    {
        var store = NewStore();
        var changes = 0;
        store.Changed += () => changes++;

        Assert.True(store.Add(Controller));
        Assert.False(store.Add(Controller));
        Assert.False(store.Remove(Other));

        Assert.Equal(1, changes);
        Assert.True(store.Contains(Controller));
    }
}
