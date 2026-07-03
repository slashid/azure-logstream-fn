using System.Text.Json;
using SlashID.LogStreamFn;
using Xunit;

public class ControlEventsTests
{
    [Fact]
    public void HeartbeatMatchesReceiverEnvelopeContract()
    {
        using var doc = JsonDocument.Parse(ControlEvents.Heartbeat());
        var env = Assert.Single(doc.RootElement.EnumerateArray().ToList());
        Assert.Equal("slashid.forwarder.heartbeat", env.GetProperty("eventType").GetString());
        Assert.True(env.TryGetProperty("eventTime", out _));
        Assert.False(string.IsNullOrEmpty(env.GetProperty("data").GetProperty("version").GetString()));
    }

    [Fact]
    public void RecordDroppedCarriesCountAndReason()
    {
        using var doc = JsonDocument.Parse(ControlEvents.RecordDropped(3, "record exceeds receiver body cap"));
        var env = Assert.Single(doc.RootElement.EnumerateArray().ToList());
        Assert.Equal("slashid.forwarder.record_dropped", env.GetProperty("eventType").GetString());
        Assert.Equal(3, env.GetProperty("data").GetProperty("droppedRecords").GetInt32());
        Assert.Equal("record exceeds receiver body cap", env.GetProperty("data").GetProperty("reason").GetString());
    }
}
