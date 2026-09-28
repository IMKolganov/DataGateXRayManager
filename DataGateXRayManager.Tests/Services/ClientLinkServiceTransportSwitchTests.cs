using System.Text;
using System.Text.Json;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;
using DataGateXRayManager.Services;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services;

/// <summary>
/// Apps re-download the profile on every connect, so a node must be able to move its users to another
/// transport (<c>XRAY_CLIENT_LINK_TRANSPORT</c>) without re-issuing credentials or editing the dashboard
/// template: <see cref="ClientLinkService.DownloadClientLink"/> re-renders from the template captured at
/// issue time.
/// </summary>
public class ClientLinkServiceTransportSwitchTests
{
    private const string Uuid = "22222222-2222-2222-2222-222222222222";
    private const string CommonName = "cn-switch";
    private const string Template = """{"vless":"{{vless_uri}}","uuid":"{{uuid}}"}""";

    [Fact]
    public async Task AddClientLink_TransportXhttp_PointsPrimaryProfileAtXhttpInbound()
    {
        using var work = new TempDir();
        var svc = Build(work, Settings(transport: "xhttp", xhttpEnabled: true));

        var meta = await svc.AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
            CancellationToken.None);

        var vless = VlessOf(await File.ReadAllTextAsync(meta.FilePath));
        Assert.Contains("@xs1.example.com:2053?", vless);
        Assert.Contains("type=xhttp", vless);
        Assert.Contains("path=%2Fapi%2Fv1%2Fupdate", vless);
    }

    [Fact]
    public async Task AddClientLink_TransportXhttpButInboundDisabled_FallsBackToPrimaryTransport()
    {
        using var work = new TempDir();
        var svc = Build(work, Settings(transport: "xhttp", xhttpEnabled: false));

        var meta = await svc.AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
            CancellationToken.None);

        var vless = VlessOf(await File.ReadAllTextAsync(meta.FilePath));
        Assert.Contains("@xs1.example.com:443?", vless);
        Assert.Contains("type=tcp", vless);
        Assert.DoesNotContain("type=xhttp", vless);
    }

    [Fact]
    public async Task DownloadClientLink_AfterTransportSwitch_ReRendersStoredFile()
    {
        using var work = new TempDir();
        var issued = await Build(work, Settings(transport: "primary", xhttpEnabled: true))
            .AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
                CancellationToken.None);
        Assert.Contains("type=tcp", VlessOf(await File.ReadAllTextAsync(issued.FilePath)));

        var afterSwitch = Build(work, Settings(transport: "xhttp", xhttpEnabled: true));
        var download = await afterSwitch.DownloadClientLink(issued.FileName, issued.FilePath, CancellationToken.None);

        var served = VlessOf(Encoding.UTF8.GetString(download.Content));
        Assert.Contains("type=xhttp", served);
        Assert.Contains($"vless://{Uuid}@xs1.example.com:2053?", served);
        // Persisted too, so operators inspecting the node see what clients actually get.
        Assert.Contains("type=xhttp", VlessOf(await File.ReadAllTextAsync(issued.FilePath)));
    }

    [Fact]
    public async Task DownloadClientLink_UnchangedSettings_ServesIdenticalContent()
    {
        using var work = new TempDir();
        var settings = Settings(transport: "primary", xhttpEnabled: true);
        var svc = Build(work, settings);
        var issued = await svc.AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
            CancellationToken.None);
        var before = await File.ReadAllTextAsync(issued.FilePath);
        var writeTime = File.GetLastWriteTimeUtc(issued.FilePath);

        var download = await svc.DownloadClientLink(issued.FileName, issued.FilePath, CancellationToken.None);

        Assert.Equal(before, Encoding.UTF8.GetString(download.Content));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(issued.FilePath));
    }

    [Fact]
    public async Task DownloadClientLink_WithoutStoredTemplate_ServesFileAsIssued()
    {
        using var work = new TempDir();
        var issued = await Build(work, Settings(transport: "primary", xhttpEnabled: true))
            .AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
                CancellationToken.None);
        var before = await File.ReadAllTextAsync(issued.FilePath);
        // Link issued by an older manager version: no render inputs on disk.
        Directory.Delete(Path.Combine(work.Path, "xray", "link-render"), recursive: true);

        var download = await Build(work, Settings(transport: "xhttp", xhttpEnabled: true))
            .DownloadClientLink(issued.FileName, issued.FilePath, CancellationToken.None);

        Assert.Equal(before, Encoding.UTF8.GetString(download.Content));
    }

    [Fact]
    public async Task DownloadClientLink_ClientNoLongerInStore_ServesFileAsIssued()
    {
        using var work = new TempDir();
        var issued = await Build(work, Settings(transport: "primary", xhttpEnabled: true))
            .AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
                CancellationToken.None);
        var before = await File.ReadAllTextAsync(issued.FilePath);

        var users = MockUsers(work);
        users.Setup(u => u.GetAllCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var svc = new ClientLinkService(NullLogger<ClientLinkService>.Instance, users.Object,
            Config(Settings(transport: "xhttp", xhttpEnabled: true)));

        var download = await svc.DownloadClientLink(issued.FileName, issued.FilePath, CancellationToken.None);

        Assert.Equal(before, Encoding.UTF8.GetString(download.Content));
    }

    [Fact]
    public async Task RevokeClientLink_DropsStoredTemplate()
    {
        using var work = new TempDir();
        var users = MockUsers(work);
        users.Setup(u => u.RevokeCertificateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerCertificate { CommonName = CommonName, SerialNumber = Uuid, IsRevoked = true, Message = "Revoked" });
        var svc = new ClientLinkService(NullLogger<ClientLinkService>.Instance, users.Object,
            Config(Settings(transport: "primary", xhttpEnabled: true)));

        var issued = await svc.AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
            CancellationToken.None);
        var renderInputs = Path.Combine(work.Path, "xray", "link-render", $"{CommonName}.json");
        Assert.True(File.Exists(renderInputs));

        await svc.RevokeClientLink(work.Path, CommonName, issued.FileName, issued.FilePath, CancellationToken.None);

        Assert.False(File.Exists(renderInputs));
    }

    [Fact]
    public async Task AddClientLink_StoredTemplate_HoldsNoCredentials()
    {
        using var work = new TempDir();
        await Build(work, Settings(transport: "primary", xhttpEnabled: true))
            .AddClientLink(work.Path, CommonName, "Helsinki", Template, "xs1.example.com", 443,
                CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(work.Path, "xray", "link-render", $"{CommonName}.json"));
        Assert.DoesNotContain(Uuid, json);
        Assert.Contains("{{vless_uri}}", json);
        Assert.Contains("xs1.example.com", json);
    }

    private static string VlessOf(string linkFileContent)
    {
        using var doc = JsonDocument.Parse(linkFileContent.Trim());
        return doc.RootElement.GetProperty("vless").GetString()!;
    }

    private static Dictionary<string, string?> Settings(string transport, bool xhttpEnabled) =>
        new()
        {
            ["XRAY_TRANSPORT_MODE"] = "plain",
            ["XRAY_CLIENT_LINK_TRANSPORT"] = transport,
            ["XRAY_XHTTP_ENABLED"] = xhttpEnabled ? "true" : "false",
            ["XRAY_XHTTP_PORT"] = "2053",
            ["XRAY_XHTTP_PATH"] = "/api/v1/update",
            ["XRAY_XHTTP_MODE"] = "auto"
        };

    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Mock<IXRayUserService> MockUsers(TempDir work)
    {
        var cert = new ServerCertificate
        {
            CommonName = CommonName,
            SerialNumber = Uuid,
            CertificatePath = Path.Combine(work.Path, "cert.pem"),
            Message = "ok"
        };
        var users = new Mock<IXRayUserService>();
        users.Setup(u => u.BuildCertificateAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(cert);
        users.Setup(u => u.GetAllCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([cert]);
        return users;
    }

    private static ClientLinkService Build(TempDir work, Dictionary<string, string?> settings) =>
        new(NullLogger<ClientLinkService>.Instance, MockUsers(work).Object, Config(settings));

    private sealed class TempDir : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("xray-link-transport-");

        public string Path => _dir.FullName;

        public void Dispose()
        {
            try
            {
                _dir.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Test teardown only.
            }
        }
    }
}
