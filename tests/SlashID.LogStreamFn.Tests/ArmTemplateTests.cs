using System.Text.Json;
using Xunit;

public class ArmTemplateTests
{
    private static JsonDocument Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../../..", "azuredeploy.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void DeclaresExpectedParameters()
    {
        using var doc = Load();
        var parameters = doc.RootElement.GetProperty("parameters");
        foreach (var p in new[] { "eventsToken", "baseName", "packageUri", "forwarderVersion" })
            Assert.True(parameters.TryGetProperty(p, out _), $"missing parameter {p}");
        Assert.Equal("securestring", parameters.GetProperty("eventsToken").GetProperty("type").GetString());
    }

    [Fact]
    public void FunctionAppCarriesTheContractAppSettings()
    {
        using var doc = Load();
        var raw = doc.RootElement.GetRawText();
        foreach (var setting in new[]
        {
            "EventHubConnection", "EVENTHUB_NAME", "EVENTHUB_CONSUMER_GROUP",
            "SLASHID_EVENTS_ENDPOINT", "SLASHID_PUSH_AUTH_TOKEN", "FORWARDER_VERSION",
        })
            Assert.Contains(setting, raw);
    }

    [Fact]
    public void OneDeployPullsThePackage()
    {
        using var doc = Load();
        Assert.Contains("onedeploy", doc.RootElement.GetRawText());
    }
}
