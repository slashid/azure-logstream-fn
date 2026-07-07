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
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var r) && r.ValueKind == JsonValueKind.Array)
            return Accumulate(r.EnumerateArray().Select(e => e.GetRawText()));
        if (root.ValueKind == JsonValueKind.Array)
            return Accumulate(root.EnumerateArray().Select(e => e.GetRawText()));

        // Oversized and unsplittable: a single JSON object bigger than the cap,
        // or a "records" property present but not an array. Deterministic drop.
        return new ChunkPlan(new List<string>(), 1);
    }

    /// <summary>Plans POST bodies for a whole Event Hub invocation batch. Parses
    /// EVERY message up front — malformed JSON throws BEFORE any POST, a strict
    /// safety improvement over per-message planning — then pools the records
    /// from all messages into as few ≤MaxChunkBytes {"records":[…]} bodies as
    /// possible, so a burst of small messages collapses into a single POST.
    /// Record extraction per message: root {"records":[…]} object → its array
    /// elements; bare array → its elements; anything else (lone object/primitive)
    /// → one implicit record. Unlike Plan there is NO verbatim zero-copy fast
    /// path: records are re-serialized into fresh envelopes. That copy is the
    /// deliberate price of coalescing; the batching win dominates.</summary>
    public static ChunkPlan PlanBatch(IReadOnlyList<string> messages)
    {
        var records = new List<string>();
        foreach (var message in messages)
        {
            using var doc = JsonDocument.Parse(message); // throws BEFORE any POST → nothing sent
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var r) && r.ValueKind == JsonValueKind.Array)
                foreach (var e in r.EnumerateArray()) records.Add(e.GetRawText());
            else if (root.ValueKind == JsonValueKind.Array)
                foreach (var e in root.EnumerateArray()) records.Add(e.GetRawText());
            else
                records.Add(root.GetRawText()); // lone object/primitive = one implicit record
        }
        return Accumulate(records);
    }

    // Shared byte-budgeted accumulate/flush. A single record that alone exceeds
    // the cap is dropped (DroppedRecords++); everything else is packed greedily
    // into ordered ≤MaxChunkBytes {"records":[…]} bodies.
    private static ChunkPlan Accumulate(IEnumerable<string> recordJsons)
    {
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

        foreach (var json in recordJsons)
        {
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
