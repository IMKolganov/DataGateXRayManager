using System.Text.Json;
using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;
using DataGateXRayManager.Services;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services;

/// <summary>
/// End-to-end link rendering for the dashboard default Xray ConfigTemplate
/// (frontend <c>XRAY_EXPORT_TEMPLATE</c>) → Android-parseable JSON profile.
/// </summary>
public class ClientLinkServiceDnsPlaceholderTests
{
    /// <summary>Keep in sync with frontend/src/utils/exportConfigTemplates.ts XRAY_EXPORT_TEMPLATE.</summary>
    public const string DashboardXrayExportTemplate =
        """{"vless":"{{vless_uri}}","dnsServers":{{dns_servers_json}},"dnsIdentityEnabled":{{dns_identity_enabled}},"friendlyName":"{{friendly_name}}","uuid":"{{uuid}}","endpoint":"{{server_ip}}:{{server_port}}"}""";

    [Fact]
    public async Task AddClientLink_ExpandsDnsPlaceholdersFromEnv()
    {
        var text = await IssueAsync(
            """{"vless":"{{vless_uri}}","dnsServers":{{dns_servers_json}},"dnsIdentityEnabled":{{dns_identity_enabled}},"dns1":"{{dns1}}","dns2":"{{dns2}}"}""",
            dns1: "172.20.0.1",
            dns2: "8.8.8.8",
            identity: true);

        Assert.Contains("""["172.20.0.1","8.8.8.8"]""", text);
        Assert.Contains("\"dnsIdentityEnabled\":true", text);
        Assert.Contains("\"dns1\":\"172.20.0.1\"", text);
        Assert.Contains("\"dns2\":\"8.8.8.8\"", text);
        Assert.Contains("vless://11111111-1111-1111-1111-111111111111@xs2.example.com:443", text);
    }

    [Fact]
    public async Task AddClientLink_DashboardDefaultTemplate_ProducesAndroidJsonProfile()
    {
        var text = await IssueAsync(
            DashboardXrayExportTemplate,
            dns1: "172.20.0.1",
            dns2: null,
            identity: true,
            friendlyName: "xs2 [Pi-hole]");

        using var doc = JsonDocument.Parse(text.Trim());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("dnsServers").ValueKind);
        Assert.Equal("172.20.0.1", root.GetProperty("dnsServers")[0].GetString());
        Assert.True(root.GetProperty("dnsIdentityEnabled").GetBoolean());
        Assert.StartsWith("vless://", root.GetProperty("vless").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("uuid").GetString());
        Assert.Equal("xs2.example.com:443", root.GetProperty("endpoint").GetString());
        Assert.Equal("xs2 [Pi-hole]", root.GetProperty("friendlyName").GetString());
    }

    [Fact]
    public void DashboardXrayExportTemplate_MatchesFrontendPlaceholderContract()
    {
        // Keep in sync with frontend/src/utils/exportConfigTemplates.ts XRAY_EXPORT_TEMPLATE.
        Assert.Contains("{{vless_uri}}", DashboardXrayExportTemplate);
        Assert.Contains("{{dns_servers_json}}", DashboardXrayExportTemplate);
        Assert.Contains("{{dns_identity_enabled}}", DashboardXrayExportTemplate);
        Assert.Contains("\"dnsServers\"", DashboardXrayExportTemplate);
        Assert.Contains("\"dnsIdentityEnabled\"", DashboardXrayExportTemplate);
        Assert.StartsWith("{", DashboardXrayExportTemplate.Trim());
    }

    [Fact]
    public async Task AddClientLink_JsonWithoutDnsPlaceholders_OmitsDnsServersKey()
    {
        var text = await IssueAsync(
            """{"vless":"{{vless_uri}}","uuid":"{{uuid}}"}""",
            dns1: "172.20.0.1",
            dns2: null,
            identity: true);

        using var doc = JsonDocument.Parse(text.Trim());
        Assert.False(doc.RootElement.TryGetProperty("dnsServers", out _));
        Assert.StartsWith("vless://", doc.RootElement.GetProperty("vless").GetString());
    }

    [Fact]
    public async Task AddClientLink_NoDnsEnv_EmitsEmptyDnsServersAndIdentityFalse()
    {
        var text = await IssueAsync(DashboardXrayExportTemplate, dns1: null, dns2: null, identity: false);

        using var doc = JsonDocument.Parse(text.Trim());
        Assert.Equal(0, doc.RootElement.GetProperty("dnsServers").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("dnsIdentityEnabled").GetBoolean());
    }

    [Fact]
    public async Task AddClientLink_LegacyPlainTemplate_LeavesDnsPlaceholdersUnused_StillEmitsVless()
    {
        var text = await IssueAsync(
            "{{vless_uri}}\n# {{friendly_name}}\nUUID: {{uuid}}\nEndpoint: {{server_ip}}:{{server_port}}\n",
            dns1: "172.20.0.1",
            dns2: null,
            identity: true);

        Assert.StartsWith("vless://", text.Trim());
        Assert.DoesNotContain("dnsServers", text);
        Assert.Contains("UUID: 11111111-1111-1111-1111-111111111111", text);
        Assert.Contains("Endpoint: xs2.example.com:443", text);
    }

    private static async Task<string> IssueAsync(
        string template,
        string? dns1,
        string? dns2,
        bool identity,
        string friendlyName = "Test [xs2]")
    {
        var work = Directory.CreateTempSubdirectory("xray-dns-link-");
        try
        {
            var pairs = new Dictionary<string, string?>
            {
                ["XRAY_TRANSPORT_MODE"] = "plain",
                ["XRAY_DNS_IDENTITY_ENABLED"] = identity ? "true" : "false",
            };
            if (dns1 is not null) pairs["DNS1"] = dns1;
            if (dns2 is not null) pairs["DNS2"] = dns2;

            var config = new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
            var users = new Mock<IXRayUserService>();
            users.Setup(u => u.BuildCertificateAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(new ServerCertificate
                {
                    SerialNumber = "11111111-1111-1111-1111-111111111111",
                    CertificatePath = Path.Combine(work.FullName, "cert.pem"),
                    KeyPath = Path.Combine(work.FullName, "key.pem"),
                    Message = "ok"
                });

            var svc = new ClientLinkService(NullLogger<ClientLinkService>.Instance, users.Object, config);
            var meta = await svc.AddClientLink(
                work.FullName,
                "cn-dns",
                friendlyName,
                template,
                "xs2.example.com",
                443,
                CancellationToken.None);

            return await File.ReadAllTextAsync(meta.FilePath);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }
}
