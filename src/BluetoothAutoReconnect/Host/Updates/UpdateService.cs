using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;

using BluetoothAutoReconnect.Tray;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BluetoothAutoReconnect.Host.Updates;

/// <summary>
/// Checks GitHub for a newer release in the background and, only when the user asks from the tray
/// menu, downloads and runs its installer. Never notifies on its own: an available update just adds
/// a menu item.
/// </summary>
internal sealed class UpdateService(
    IOptions<UpdateOptions> options,
    TrayApplicationContext tray,
    SynchronizationContext uiContext,
    ILogger<UpdateService> logger) : BackgroundService
{
    // Declared before Http: static initializers run in textual order and CreateClient reads it.
    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    private ReleaseInfo? _available;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DeleteDownloadedInstallers();
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Update checks disabled");
            return;
        }

        uiContext.Post(_ => tray.SetUpdateHandler(InstallAsync), null);
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAsync(stoppingToken);
            await Task.Delay(options.Value.CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.github.com/repos/{options.Value.Repository}/releases/latest";
            using var response = await Http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 404 = no release published yet; 403 = rate limited. Neither is worth surfacing.
                logger.LogInformation("Update check: GitHub answered {Status}", (int)response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = ReleaseParser.ParseIfNewer(json, CurrentVersion);
            if (release is null)
            {
                logger.LogInformation("Update check: {Version} is up to date", CurrentVersion);
                return;
            }

            if (_available?.Version != release.Version)
            {
                logger.LogInformation("Update available: {Tag} (running {Version})", release.Tag, CurrentVersion);
            }

            _available = release;
            uiContext.Post(_ => tray.ShowUpdateAvailable(release.Version), null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // An update check must never take the agent down (a throwing BackgroundService stops
            // the host by default), so every failure is just logged.
            logger.LogWarning(ex, "Update check failed");
        }
    }

    /// <summary>
    /// Downloads the installer, verifies it, runs it silently and returns <c>true</c> so the tray can
    /// exit (the installer replaces the files and starts the new version).
    /// </summary>
    private async Task<bool> InstallAsync(IProgress<int> progress)
    {
        if (_available is not { } release)
        {
            return false;
        }

        var path = Path.Combine(Path.GetTempPath(), $"BluetoothAutoReconnect-Setup-{release.Version}.exe");
        try
        {
            logger.LogInformation("Downloading {Url}", release.InstallerUrl);
            await DownloadAsync(release, path, progress);

            if (release.Sha256 is { } expected)
            {
                var actual = Convert.ToHexStringLower(await HashFileAsync(path));
                if (actual != expected)
                {
                    throw new InvalidDataException($"SHA-256 mismatch: expected {expected}, got {actual}");
                }
            }

            logger.LogInformation("Starting installer for {Tag}", release.Tag);
            // /SILENT shows progress but asks nothing; the installer closes this process and starts
            // the new version when it is done.
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update to {Tag} failed", release.Tag);
            TryDelete(path);
            return false;
        }
    }

    private static async Task DownloadAsync(ReleaseInfo release, string path, IProgress<int> progress)
    {
        using var response = await Http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.InstallerSize;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(path);
        var buffer = new byte[81920];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read));
            written += read;
            if (total > 0)
            {
                progress.Report((int)(written * 100 / total));
            }
        }

        if (release.InstallerSize > 0 && written != release.InstallerSize)
        {
            throw new InvalidDataException($"Downloaded {written} bytes, expected {release.InstallerSize}");
        }
    }

    private static async Task<byte[]> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream);
    }

    /// <summary>Installers from previous updates are no longer needed once the new version runs.</summary>
    private static void DeleteDownloadedInstallers()
    {
        foreach (var path in Directory.EnumerateFiles(Path.GetTempPath(), "BluetoothAutoReconnect-Setup-*.exe"))
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use or locked; the next start retries.
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BluetoothAutoReconnect", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
