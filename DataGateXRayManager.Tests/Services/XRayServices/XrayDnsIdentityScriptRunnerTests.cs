using System.Diagnostics;
using DataGateXRayManager.Services.XRayServices;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public class XrayDnsIdentityScriptRunnerTests
{
    [Fact]
    public async Task RunAsync_CompletesForQuickScript()
    {
        var script = WriteScript("""
            #!/bin/bash
            set -euo pipefail
            echo "ok-$CLIENTS_JSON"
            echo "iface=$XRAY_DNS_IDENTITY_IFACE" >&2
            exit 0
            """);

        var runner = new ProcessXrayDnsIdentityScriptRunner();
        var result = await runner.RunAsync(new XrayDnsIdentityScriptRequest
        {
            ScriptPath = script,
            ConfigPath = "/tmp/cfg.json",
            PidFile = "/tmp/xray.pid",
            Iface = "eth0",
            Subnet = "10.80.0.0/24",
            ClientsJson = """[{"commonName":"a","identityIp":"10.80.0.2"}]"""
        }, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ok-", result.Stdout);
        Assert.Contains("iface=eth0", result.Stderr);
    }

    [Fact]
    public async Task RunAsync_TimesOutHungScriptThatIgnoresTerm()
    {
        if (OperatingSystem.IsWindows())
            return;

        var script = WriteScript("""
            #!/bin/bash
            trap '' TERM
            sleep 600
            """);

        var runner = new ProcessXrayDnsIdentityScriptRunner(TimeSpan.FromSeconds(2));
        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            runner.RunAsync(new XrayDnsIdentityScriptRequest
            {
                ScriptPath = script,
                ConfigPath = "/tmp/cfg.json",
                PidFile = "/tmp/xray.pid",
                Iface = "eth0",
                Subnet = "10.80.0.0/24",
                ClientsJson = "[]"
            }, CancellationToken.None));

        Assert.Contains("timed out", ex.Message);
    }

    private static string WriteScript(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dns-id-test-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, body.Replace("\r\n", "\n"));
        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/chmod",
            ArgumentList = { "+x", path },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!.WaitForExit(5000);

        return path;
    }
}
