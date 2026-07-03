using System.Text.Json;

namespace SlashID.LogStreamFn;

/// <summary>Builders for the reserved slashid.forwarder.* control events.
/// They ride the data channel (customer App Insights is unreachable to
/// SlashID) as single-element arrays of Event Grid-shaped envelopes, which
/// the azure-monitor-logs receiver accepts alongside raw records bodies.</summary>
public static class ControlEvents
{
    public const string HeartbeatType = "slashid.forwarder.heartbeat";
    public const string RecordDroppedType = "slashid.forwarder.record_dropped";

    public static string Heartbeat() => Wrap(HeartbeatType, new Dictionary<string, object>
    {
        ["version"] = ForwarderVersion.Current,
    });

    public static string RecordDropped(int count, string reason) => Wrap(RecordDroppedType,
        new Dictionary<string, object>
        {
            ["droppedRecords"] = count,
            ["reason"] = reason,
            ["version"] = ForwarderVersion.Current,
        });

    private static string Wrap(string eventType, Dictionary<string, object> data)
    {
        var envelope = new Dictionary<string, object>
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["subject"] = "Forwarder",
            ["eventType"] = eventType,
            ["eventTime"] = DateTimeOffset.UtcNow.ToString("O"),
            ["data"] = data,
            ["dataVersion"] = "1.0",
        };
        return "[" + JsonSerializer.Serialize(envelope) + "]";
    }
}
