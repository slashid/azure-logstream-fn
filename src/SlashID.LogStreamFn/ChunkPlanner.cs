using System.Text;
using System.Text.Json;

namespace SlashID.LogStreamFn;

public record ChunkPlan(List<string> Bodies, int DroppedRecords);

public static class ChunkPlanner
{
    // Receiver body cap is 1 MiB (readHTTPRequest, channels.go); 768 KB leaves headroom.
    public const int MaxChunkBytes = 768 * 1024;

    // JSON overhead of the {"records":[]} wrapper.
    private const int EnvelopeOverhead = 14; // {"records":[]}

    /// <summary>Plans the POST bodies for one Event Hub message. Fast path: a
    /// well-formed body that fits is forwarded verbatim (zero-copy). Oversized
    /// bodies are split into {"records":[…]} chunks. A single record that alone
    /// exceeds the cap is counted in DroppedRecords (the design's only drop
    /// case). Malformed JSON throws: the outer retry policy holds the checkpoint.</summary>
    public static ChunkPlan Plan(string messageBody)
    {
        using var doc = JsonDocument.Parse(messageBody); // throws JsonException on malformed
        if (Encoding.UTF8.GetByteCount(messageBody) <= MaxChunkBytes)
            return new ChunkPlan(new List<string> { messageBody }, 0);

        var root = doc.RootElement;
        JsonElement records;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var r))
            records = r;
        else if (root.ValueKind == JsonValueKind.Array)
            records = root;
        else
            // A single JSON object bigger than the cap: pathological; drop it.
            return new ChunkPlan(new List<string>(), 1);

        var bodies = new List<string>();
        var dropped = 0;
        var current = new List<string>();
        var currentBytes = EnvelopeOverhead;

        void Flush()
        {
            if (current.Count == 0) return;
            bodies.Add($$"""{"records":[{{string.Join(",", current)}}]}""");
            current = new List<string>();
            currentBytes = EnvelopeOverhead;
        }

        foreach (var record in records.EnumerateArray())
        {
            var json = record.GetRawText();
            var bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes + EnvelopeOverhead > MaxChunkBytes) { dropped++; continue; }
            if (current.Count > 0 && currentBytes + bytes + 1 > MaxChunkBytes) Flush();
            current.Add(json);
            currentBytes += bytes + (current.Count > 1 ? 1 : 0);
        }
        Flush();

        return new ChunkPlan(bodies, dropped);
    }
}
