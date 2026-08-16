using DataGateMonitor.SharedModels.DataGateXRayManager.Cert.Responses;
using DataGateXRayManager.Services;
using DataGateXRayManager.Services.Interfaces;
using DataGateXRayManager.Services.XRayServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateXRayManager.Tests.Services;

public class ClientLinkServiceDnsPlaceholderTests
{
    [Fact]
    public async Task AddClientLink_ExpandsDnsPlaceholdersFromEnv()
    {
        var work = Directory.CreateTempSubdirectory("xray-dns-link-");
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DNS1"] = "172.20.0.1",
                ["DNS2"] = "8.8.8.8",
                ["XRAY_DNS_IDENTITY_ENABLED"] = "true",
                ["XRAY_TRANSPORT_MODE"] = "plain",
            }).Build();

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
            const string template =
                """{"vless":"{{vless_uri}}","dnsServers":{{dns_servers_json}},"dnsIdentityEnabled":{{dns_identity_enabled}},"dns1":"{{dns1}}","dns2":"{{dns2}}"}""";

            var meta = await svc.AddClientLink(
                work.FullName,
                "cn-dns",
                "Test [xs2]",
                template,
                "xs2.example.com",
                443,
                CancellationToken.None);

            var text = await File.ReadAllTextAsync(meta.FilePath);
            Assert.Contains("""["172.20.0.1","8.8.8.8"]""", text);
            Assert.Contains("\"dnsIdentityEnabled\":true", text);
            Assert.Contains("\"dns1\":\"172.20.0.1\"", text);
            Assert.Contains("\"dns2\":\"8.8.8.8\"", text);
            Assert.Contains("vless://11111111-1111-1111-1111-111111111111@xs2.example.com:443", text);
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }
}
