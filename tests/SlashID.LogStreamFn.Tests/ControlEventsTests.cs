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

    [Fact]
    public void EnvelopeIsAProperSingleElementJsonArray_NotStringConcatenated()
    {
        // Guards against regressing to "[" + Serialize(envelope) + "]" string
        // concatenation, which is not guaranteed to emit well-formed JSON for
        // arbitrary content; the envelope must be produced by serializing an
        // actual one-element array so escaping is always handled correctly.
        var reason = "reason with \"quotes\", [brackets], a backslash \\, and é unicode";
        var json = ControlEvents.RecordDropped(2, reason);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        var env = Assert.Single(doc.RootElement.EnumerateArray().ToList());
        Assert.Equal(reason, env.GetProperty("data").GetProperty("reason").GetString());
    }
}
