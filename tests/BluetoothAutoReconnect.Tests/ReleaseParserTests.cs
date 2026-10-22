using BluetoothAutoReconnect.Host.Updates;

namespace BluetoothAutoReconnect.Tests;

public class ReleaseParserTests
{
    private static readonly Version Current = new(1, 0, 0);
    private const string Hex = "584878e75efea9e414a7d3515a02a1f9fc81513f3f179546dc7cf938bddcb72d";

    private static string Release(
        string tag = "v1.1.0",
        bool draft = false,
        bool prerelease = false,
        string assetName = "BluetoothAutoReconnect-Setup-1.1.0.exe",
        string url = "https://github.com/owner/repo/releases/download/v1.1.0/BluetoothAutoReconnect-Setup-1.1.0.exe",
        string? digest = "sha256:" + Hex) => $$"""
        {
          "tag_name": "{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.com/notes.txt", "size": 10 },
            {
              "name": "{{assetName}}",
              "browser_download_url": "{{url}}",
              "size": 51603159
              {{(digest is null ? "" : $", \"digest\": \"{digest}\"")}}
            }
          ]
        }
        """;

    [Fact]
    public void ReturnsNewerReleaseWithInstaller()
    {
        var info = ReleaseParser.ParseIfNewer(Release(), Current);

        Assert.NotNull(info);
        Assert.Equal(new Version(1, 1, 0), info.Version);
        Assert.Equal("v1.1.0", info.Tag);
        Assert.EndsWith("BluetoothAutoReconnect-Setup-1.1.0.exe", info.InstallerUrl.AbsoluteUri);
        Assert.Equal(51603159, info.InstallerSize);
        Assert.Equal(Hex, info.Sha256);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("1.0")]
    [InlineData("v0.9.5")]
    public void IgnoresSameOrOlderVersion(string tag) =>
        Assert.Null(ReleaseParser.ParseIfNewer(Release(tag: tag), Current));

    [Fact]
    public void ComparesAgainstFourPartAssemblyVersion() =>
        Assert.Null(ReleaseParser.ParseIfNewer(Release(tag: "v1.0.0"), new Version(1, 0, 0, 0)));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IgnoresDraftsAndPrereleases(bool draft, bool prerelease) =>
        Assert.Null(ReleaseParser.ParseIfNewer(Release(draft: draft, prerelease: prerelease), Current));

    [Theory]
    [InlineData("latest")]
    [InlineData("v2")]
    public void IgnoresUnparsableTags(string tag) =>
        Assert.Null(ReleaseParser.ParseIfNewer(Release(tag: tag), Current));

    [Fact]
    public void IgnoresReleaseWithoutInstaller() =>
        Assert.Null(ReleaseParser.ParseIfNewer(Release(assetName: "BluetoothAutoReconnect.zip"), Current));

    [Fact]
    public void RejectsNonHttpsInstallerUrl() =>
        Assert.Null(ReleaseParser.ParseIfNewer(Release(url: "http://example.com/BluetoothAutoReconnect-Setup-1.1.0.exe"), Current));

    [Fact]
    public void DigestIsOptional() =>
        Assert.Null(ReleaseParser.ParseIfNewer(Release(digest: null), Current)!.Sha256);
}
