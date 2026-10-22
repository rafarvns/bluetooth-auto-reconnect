using BluetoothAutoReconnect.Devices;
using BluetoothAutoReconnect.Host;
using BluetoothAutoReconnect.Host.Updates;
using BluetoothAutoReconnect.Reconnect;
using BluetoothAutoReconnect.Tray;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;

namespace BluetoothAutoReconnect;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Two agents would race each other's pairing attempts and fight over the log file.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\BluetoothAutoReconnect", out var isFirstInstance);
        if (!isFirstInstance && !WaitForPreviousInstance(singleInstance))
        {
            MessageBox.Show(
                "O Bluetooth Auto Reconnect já está rodando — procure o ícone na bandeja do sistema.",
                "Bluetooth Auto Reconnect",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        // Created on the UI thread so the WindowsFormsSynchronizationContext is installed.
        using var tray = new TrayApplicationContext();
        var uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("No UI synchronization context.");

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
#if LOG_WINDOW
        var logBuffer = new LogBuffer();
        builder.Services.AddSingleton(logBuffer);
        builder.Services.AddSerilog(config => config.ReadFrom.Configuration(builder.Configuration).WriteTo.Sink(logBuffer));
#else
        builder.Services.AddSerilog(config => config.ReadFrom.Configuration(builder.Configuration));
#endif
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
        builder.Services.AddSingleton(tray);
        builder.Services.AddSingleton(uiContext);
        builder.Services.AddSingleton(sp => new ManagedDeviceStore(
            sp.GetRequiredService<IOptions<AgentOptions>>().Value.ManagedDevices,
            ManagedDeviceStore.DefaultFilePath,
            sp.GetRequiredService<ILogger<ManagedDeviceStore>>()));
        builder.Services.AddSingleton<IDeviceSource, WinRtDeviceSource>();
        builder.Services.AddSingleton<AdvertisementMonitor>();
        builder.Services.AddSingleton<PairingService>();
        builder.Services.AddSingleton(_ => AdvertisementReactor.CreateDefault());
        builder.Services.AddSingleton<MaintainConnectionStrategy>();
        builder.Services.AddSingleton<AdvertisementConnectStrategy>();
        builder.Services.AddSingleton<IReconnectStrategy>(sp => sp.GetRequiredService<MaintainConnectionStrategy>());
        builder.Services.AddSingleton<IReconnectStrategy>(sp => sp.GetRequiredService<AdvertisementConnectStrategy>());
        builder.Services.AddHostedService<DeviceMonitor>();
        builder.Services.Configure<UpdateOptions>(builder.Configuration.GetSection(UpdateOptions.SectionName));
        builder.Services.AddHostedService<UpdateService>();

        using var host = builder.Build();
        tray.SetPairingWindowFactory(() => ActivatorUtilities.CreateInstance<PairingForm>(host.Services));
#if LOG_WINDOW
        tray.SetLogWindowFactory(() => ActivatorUtilities.CreateInstance<LogForm>(host.Services));
#endif
        try
        {
            host.Start();
            Application.Run(tray);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Agent terminated unexpectedly");
            return 1;
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// After an in-app update the installer starts the new version while the old one may still be
    /// shutting down, so give it a moment to release the mutex before declaring a duplicate.
    /// </summary>
    private static bool WaitForPreviousInstance(Mutex singleInstance)
    {
        try
        {
            return singleInstance.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (AbandonedMutexException)
        {
            // The previous instance exited without releasing it; ownership is ours now.
            return true;
        }
    }
}
