using SlashID.LogStreamFn;
using Xunit;

public class ForwarderVersionTests
{
    [Fact]
    public void EnvVarWinsAndStripsLeadingV()
    {
        Assert.Equal("1.2.3", ForwarderVersion.Resolve("v1.2.3", () => "9.9.9"));
        Assert.Equal("1.2.3", ForwarderVersion.Resolve("1.2.3", () => "9.9.9"));
    }

    [Fact]
    public void FallsBackToAssemblyVersion()
    {
        Assert.Equal("9.9.9", ForwarderVersion.Resolve(null, () => "9.9.9"));
        Assert.Equal("9.9.9", ForwarderVersion.Resolve("", () => "9.9.9"));
    }

    [Fact]
    public void LastResortIsDevSentinel()
    {
        Assert.Equal("0.0.0-dev", ForwarderVersion.Resolve(null, () => null));
    }
}
