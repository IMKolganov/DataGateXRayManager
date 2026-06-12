using DataGateXRayManager.Services.XRayServices;

namespace DataGateXRayManager.Tests.Services.XRayServices;

public sealed class XRayCliErrorSupportTests
{
    [Theory]
    [InlineData("flag provided but not defined: -all", "-all", true)]
    [InlineData("xray api failed (2): flag provided but not defined: -include-traffic", "-include-traffic", true)]
    [InlineData("flag provided but not defined: -email", "-email", true)]
    [InlineData("failed to dial 127.0.0.1:10085: connection refused", "-all", false)]
    [InlineData("flag provided but not defined: -all", "-include-traffic", false)]
    [InlineData("", "-all", false)]
    public void LooksLikeUndefinedCliFlag_MatchesGoFlagParserOutput(string message, string flag, bool expected) =>
        Assert.Equal(expected, XRayCliErrorSupport.LooksLikeUndefinedCliFlag(message, flag));
}
