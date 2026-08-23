using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DataGateXRayManager.Tests.Services.XRayServices;

/// <summary>
/// Black-box checks for <c>sync-dns-identity.sh</c> jq transform + idempotent alias field parsing,
/// without requiring NET_ADMIN or a live Xray (those live in the Docker smoke script).
/// </summary>
public class SyncDnsIdentityScriptUnitTests
{
    private static string ScriptPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "scripts", "xray", "sync-dns-identity.sh"));

    [Fact]
    public void Script_UsesIpDashOField4_ForAliases()
    {
        Assert.True(File.Exists(ScriptPath), $"missing script at {ScriptPath}");
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("awk '{print $4}'", text);
        Assert.Contains("nohup xray run", text);
        // Comments may mention -test; the script must not execute it.
        Assert.False(
            Regex.IsMatch(text, @"^\s*(timeout\s+\S+\s+)?xray\s+run\s+-test\b", RegexOptions.Multiline),
            "script must not execute xray run -test");
    }

    [Fact]
    public async Task JqTransform_AddsSendThroughRules_AndIsIdempotent()
    {
        if (!HasCommand("jq"))
            return; // skip on hosts without jq

        var work = Directory.CreateTempSubdirectory("dns-id-jq-");
        try
        {
            var configPath = Path.Combine(work.FullName, "config.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "inbounds": [{"tag":"api"}],
                  "outbounds": [{"protocol":"freedom","tag":"direct"}],
                  "routing": {"rules":[{"type":"field","inboundTag":["api"],"outboundTag":"api"}]}
                }
                """);

            var clients = """[{"commonName":"cn-a","identityIp":"10.80.0.2"},{"commonName":"cn-b","identityIp":"10.80.0.3"}]""";
            var once = await RunJqTransformAsync(configPath, clients);
            Assert.Contains("dns-id-10-80-0-2", once);
            Assert.Contains("\"sendThrough\": \"10.80.0.2\"", once);
            Assert.Contains("\"port\": \"53\"", once);

            await File.WriteAllTextAsync(configPath, once);
            var twice = await RunJqTransformAsync(configPath, clients);
            var dnsOutbounds = Regex.Matches(twice, "\"tag\": \"dns-id-").Count;
            Assert.Equal(2, dnsOutbounds);
        }
        finally
        {
            try { work.Delete(true); } catch { /* ignore */ }
        }
    }

    private static async Task<string> RunJqTransformAsync(string configPath, string clientsJson)
    {
        // Mirror the jq program from sync-dns-identity.sh (keep in sync when editing the script).
        const string program = """
            .outbounds = ((.outbounds // []) | map(select((.tag // "") | startswith($prefix) | not)))
            | .routing.rules = ((.routing.rules // []) | map(select((.outboundTag // "") | startswith($prefix) | not)))
            | . as $root
            | reduce $clients[] as $c (
                $root;
                if ($c.identityIp != null and $c.identityIp != "" and $c.commonName != null and $c.commonName != "") then
                  ($prefix + ($c.identityIp | gsub("\\."; "-"))) as $tag
                  | .outbounds += [{
                      protocol: "freedom",
                      tag: $tag,
                      sendThrough: $c.identityIp,
                      settings: {}
                    }]
                  | .routing.rules = (
                      [
                        {
                          type: "field",
                          user: [$c.commonName],
                          port: "53",
                          network: "tcp,udp",
                          outboundTag: $tag
                        }
                      ] + .routing.rules
                    )
                else . end
              )
            """;

        var psi = new ProcessStartInfo
        {
            FileName = "jq",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--argjson");
        psi.ArgumentList.Add("clients");
        psi.ArgumentList.Add(clientsJson);
        psi.ArgumentList.Add("--arg");
        psi.ArgumentList.Add("prefix");
        psi.ArgumentList.Add("dns-id-");
        psi.ArgumentList.Add(program);
        psi.ArgumentList.Add(configPath);

        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        Assert.True(proc.ExitCode == 0, stderr);
        return stdout;
    }

    private static bool HasCommand(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { "-lc", $"command -v {name}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p!.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
