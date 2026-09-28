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
        """
        {
          "vless": "{{vless_uri}}",
          "vlessXhttp": "{{vless_uri_xhttp}}",
          "dnsServers": {{dns_servers_json}},
          "dnsIdentityEnabled": {{dns_identity_enabled}},
          "friendlyName": "{{friendly_name}}",
          "uuid": "{{uuid}}",
          "endpoint": "{{server_ip}}:{{server_port}}"
        }
        """;

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
        // Nodes without the extra transport must still produce a valid profile.
        Assert.Equal("", root.GetProperty("vlessXhttp").GetString());
    }

    [Fact]
    public async Task AddClientLink_DashboardDefaultTemplate_CarriesXhttpProfileWhenNodeHasIt()
    {
        var text = await IssueAsync(
            DashboardXrayExportTemplate,
            dns1: "172.20.0.1",
            dns2: null,
            identity: true,
            transportMode: "tls",
            xhttpEnabled: true,
            xhttpPort: "2053",
            xhttpPath: "/api/v1/update",
            xhttpMode: "auto");

        using var doc = JsonDocument.Parse(text.Trim());
        var root = doc.RootElement;
        Assert.Contains("type=tcp", root.GetProperty("vless").GetString());
        Assert.Contains("type=xhttp", root.GetProperty("vlessXhttp").GetString());
        Assert.Contains("@xs2.example.com:2053?", root.GetProperty("vlessXhttp").GetString());
    }

    [Fact]
    public void DashboardXrayExportTemplate_MatchesFrontendPlaceholderContract()
    {
        // Keep in sync with frontend/src/utils/exportConfigTemplates.ts XRAY_EXPORT_TEMPLATE.
        Assert.Contains("{{vless_uri}}", DashboardXrayExportTemplate);
        Assert.Contains("{{vless_uri_xhttp}}", DashboardXrayExportTemplate);
        Assert.Contains("\"vlessXhttp\"", DashboardXrayExportTemplate);
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

    [Fact]
    public async Task AddClientLink_StripsHttpsMistakenServerIp_InTlsProfile()
    {
        var text = await IssueAsync(
            DashboardXrayExportTemplate,
            dns1: "10.51.44.1",
            dns2: null,
            identity: true,
            friendlyName: "Helsinki xray",
            serverIp: "https://xs1-hel.datagateapp.com",
            serverPort: 443,
            transportMode: "tls");

        using var doc = JsonDocument.Parse(text.Trim());
        var vless = doc.RootElement.GetProperty("vless").GetString()!;
        Assert.StartsWith("vless://", vless);
        Assert.Contains("@xs1-hel.datagateapp.com:443", vless);
        Assert.DoesNotContain("@https://", vless);
        Assert.Equal("xs1-hel.datagateapp.com:443", doc.RootElement.GetProperty("endpoint").GetString());
    }

    [Fact]
    public async Task AddClientLink_XhttpEnabled_ExpandsVlessUriXhttpAndMetaPlaceholders()
    {
        var text = await IssueAsync(
            """{"vless":"{{vless_uri}}","vlessXhttp":"{{vless_uri_xhttp}}","xhttpPort":"{{xhttp_port}}","xhttpPath":"{{xhttp_path}}"}""",
            dns1: null,
            dns2: null,
            identity: false,
            transportMode: "tls",
            xhttpEnabled: true,
            xhttpPort: "2053",
            xhttpPath: "/api/v1/update",
            xhttpMode: "auto");

        using var doc = JsonDocument.Parse(text.Trim());
        var xhttp = doc.RootElement.GetProperty("vlessXhttp").GetString()!;
        Assert.StartsWith("vless://11111111-1111-1111-1111-111111111111@xs2.example.com:2053?", xhttp);
        Assert.Contains("type=xhttp", xhttp);
        Assert.Contains("security=tls", xhttp);
        Assert.Contains("alpn=h2", xhttp);
        Assert.Contains("path=%2Fapi%2Fv1%2Fupdate", xhttp);
        Assert.Contains("mode=auto", xhttp);
        Assert.Contains("sni=xs2.example.com", xhttp);
        Assert.EndsWith("#DataGate+Test+xHTTP", xhttp);
        Assert.Equal("2053", doc.RootElement.GetProperty("xhttpPort").GetString());
        Assert.Equal("/api/v1/update", doc.RootElement.GetProperty("xhttpPath").GetString());
        Assert.Contains("@xs2.example.com:443", doc.RootElement.GetProperty("vless").GetString());
    }

    [Fact]
    public async Task AddClientLink_XhttpDisabled_EmitsEmptyXhttpPlaceholders()
    {
        var text = await IssueAsync(
            """{"vlessXhttp":"{{vless_uri_xhttp}}","xhttpPort":"{{xhttp_port}}","xhttpPath":"{{xhttp_path}}"}""",
            dns1: null,
            dns2: null,
            identity: false,
            xhttpEnabled: false);

        using var doc = JsonDocument.Parse(text.Trim());
        Assert.Equal("", doc.RootElement.GetProperty("vlessXhttp").GetString());
        Assert.Equal("", doc.RootElement.GetProperty("xhttpPort").GetString());
        Assert.Equal("", doc.RootElement.GetProperty("xhttpPath").GetString());
    }

    [Fact]
    public async Task AddClientLink_XhttpPlaceholderWithoutEnabledFlag_EmitsEmptyUri()
    {
        var text = await IssueAsync(
            """{"vlessXhttp":"{{vless_uri_xhttp}}"}""",
            dns1: null,
            dns2: null,
            identity: false);

        using var doc = JsonDocument.Parse(text.Trim());
        Assert.Equal("", doc.RootElement.GetProperty("vlessXhttp").GetString());
    }

    private static async Task<string> IssueAsync(
        string template,
        string? dns1,
        string? dns2,
        bool identity,
        string friendlyName = "Test [xs2]",
        string serverIp = "xs2.example.com",
        int serverPort = 443,
        string transportMode = "plain",
        bool? xhttpEnabled = null,
        string? xhttpPort = null,
        string? xhttpPath = null,
        string? xhttpMode = null)
    {
        var work = Directory.CreateTempSubdirectory("xray-dns-link-");
        try
        {
            // Pin SNI / transport in configuration so parallel announce tests that mutate
            // XRAY__DOMAIN / XRAY_CLIENT_LINK_TRANSPORT env cannot change the rendered URI.
            var pairs = new Dictionary<string, string?>
            {
                ["XRAY_TRANSPORT_MODE"] = transportMode,
                ["XRAY_DNS_IDENTITY_ENABLED"] = identity ? "true" : "false",
                ["XRAY:DOMAIN"] = serverIp,
                ["XRAY_CLIENT_LINK_TRANSPORT"] = "primary",
            };
            if (dns1 is not null) pairs["DNS1"] = dns1;
            if (dns2 is not null) pairs["DNS2"] = dns2;
            if (xhttpEnabled is not null)
                pairs["XRAY_XHTTP_ENABLED"] = xhttpEnabled.Value ? "true" : "false";
            if (xhttpPort is not null) pairs["XRAY_XHTTP_PORT"] = xhttpPort;
            if (xhttpPath is not null) pairs["XRAY_XHTTP_PATH"] = xhttpPath;
            if (xhttpMode is not null) pairs["XRAY_XHTTP_MODE"] = xhttpMode;

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
                serverIp,
                serverPort,
                CancellationToken.None);

            return await File.ReadAllTextAsync(meta.FilePath);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }
}
