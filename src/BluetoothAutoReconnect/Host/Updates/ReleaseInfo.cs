using System.Globalization;
using System.Text.Json;

namespace BluetoothAutoReconnect.Host.Updates;

/// <summary>A newer release with an installer we can run.</summary>
/// <param name="Sha256">Lower-case hex digest from the GitHub asset, when GitHub provides one.</param>
public sealed record ReleaseInfo(Version Version, string Tag, Uri InstallerUrl, long InstallerSize, string? Sha256);

/// <summary>
/// Reads the GitHub "latest release" response. Pure: no I/O, so the update decision is unit tested.
/// </summary>
public static class ReleaseParser
{
    private const string InstallerSuffix = ".exe";
    private const string InstallerMarker = "-Setup-";

    /// <summary>
    /// Returns the release when it is newer than <paramref name="current"/> and ships an installer;
    /// otherwise <c>null</c> (same or older version, draft/prerelease, unparsable tag, no installer).
    /// </summary>
    public static ReleaseInfo? ParseIfNewer(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (Bool(root, "draft") || Bool(root, "prerelease")
            || !root.TryGetProperty("tag_name", out var tagElement)
            || tagElement.GetString() is not { } tag
            || !TryParseVersion(tag, out var version)
            || Normalize(version) <= Normalize(current))
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null
                || !name.EndsWith(InstallerSuffix, StringComparison.OrdinalIgnoreCase)
                || !name.Contains(InstallerMarker, StringComparison.OrdinalIgnoreCase)
                || !asset.TryGetProperty("browser_download_url", out var url)
                || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes) ? bytes : 0;
            return new ReleaseInfo(version, tag, uri, size, Sha256(asset));
        }

        return null;
    }

    /// <summary>Accepts <c>v1.2.3</c> or <c>1.2.3</c> (two to four components).</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out version!) && text.Count(c => c == '.') >= 1;
    }

    /// <summary>Missing components count as zero, so 1.2 == 1.2.0 == 1.2.0.0.</summary>
    private static Version Normalize(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Sha256(JsonElement asset)
    {
        // GitHub exposes "digest": "sha256:<hex>" on release assets.
        if (asset.TryGetProperty("digest", out var digest)
            && digest.GetString() is { } text
            && text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = text["sha256:".Length..].ToLower(CultureInfo.InvariantCulture);
            return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
        }

        return null;
    }
}
