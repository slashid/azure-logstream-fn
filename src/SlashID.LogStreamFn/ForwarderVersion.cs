using System.Reflection;

namespace SlashID.LogStreamFn;

public static class ForwarderVersion
{
    /// <summary>The version reported in heartbeats. Bare semver (no leading v):
    /// FORWARDER_VERSION app setting → CI-stamped assembly version → dev sentinel.</summary>
    public static string Current { get; } = Resolve(
        Environment.GetEnvironmentVariable("FORWARDER_VERSION"),
        () => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0]);

    public static string Resolve(string? envValue, Func<string?> assemblyVersion)
    {
        var v = string.IsNullOrEmpty(envValue) ? assemblyVersion() : envValue;
        if (string.IsNullOrEmpty(v)) return "0.0.0-dev";
        return v.StartsWith('v') ? v[1..] : v;
    }
}
