using System.Text;
using System.Text.Json;
using SlashID.LogStreamFn;
using Xunit;

public class ChunkPlannerTests
{
    private static string Records(params string[] recs) =>
        $$"""{"records":[{{string.Join(",", recs)}}]}""";

    private static string Rec(int seq, int padBytes) =>
        $$"""{"seq":{{seq}},"pad":"{{new string('x', padBytes)}}"}""";

    [Fact]
    public void SmallBodyForwardedVerbatim()
    {
        var body = Records(Rec(0, 100), Rec(1, 100));
        var plan = ChunkPlanner.Plan(body);
        Assert.Equal(new[] { body }, plan.Bodies); // byte-identical, not re-serialized
        Assert.Equal(0, plan.DroppedRecords);
    }

    [Fact]
    public void OversizedBodySplitsIntoRecordsEnvelopes()
    {
        var recs = Enumerable.Range(0, 20).Select(i => Rec(i, 100_000)).ToArray();
        var plan = ChunkPlanner.Plan(Records(recs));
        Assert.True(plan.Bodies.Count > 1);
        foreach (var b in plan.Bodies)
        {
            Assert.True(Encoding.UTF8.GetByteCount(b) <= ChunkPlanner.MaxChunkBytes);
            using var doc = JsonDocument.Parse(b);
            Assert.True(doc.RootElement.TryGetProperty("records", out _)); // shape preserved
        }
        var seqs = plan.Bodies
            .SelectMany(b => JsonDocument.Parse(b).RootElement.GetProperty("records")
                .EnumerateArray().Select(r => r.GetProperty("seq").GetInt32()).ToList())
            .ToList();
        Assert.Equal(Enumerable.Range(0, 20).ToList(), seqs); // complete, ordered
    }

    [Fact]
    public void BareArrayBodySupported()
    {
        var body = $"[{Rec(0, 100_000)},{Rec(1, 700_000)},{Rec(2, 100_000)}]";
        var plan = ChunkPlanner.Plan(body);
        Assert.True(plan.Bodies.Count >= 2);
        Assert.Equal(0, plan.DroppedRecords);
    }

    [Fact]
    public void SingleOversizedRecordIsDropped()
    {
        var plan = ChunkPlanner.Plan(Records(Rec(0, 100), Rec(1, 900_000), Rec(2, 100)));
        Assert.Equal(1, plan.DroppedRecords);
        var total = plan.Bodies.Sum(b =>
            JsonDocument.Parse(b).RootElement.GetProperty("records").GetArrayLength());
        Assert.Equal(2, total);
    }

    [Fact]
    public void SingleObjectBodyForwardedVerbatim()
    {
        var body = """{"operationName":"Sign-in activity","category":"SignInLogs"}""";
        Assert.Equal(new[] { body }, ChunkPlanner.Plan(body).Bodies);
    }

    [Fact]
    public void MalformedJsonThrows()
    {
        Assert.ThrowsAny<JsonException>(() => ChunkPlanner.Plan("not json"));
    }

    [Fact]
    public void SingleOversizedObjectBodyIsDropped()
    {
        var body = $$"""{"operationName":"Sign-in activity","pad":"{{new string('x', 900_000)}}"}""";
        var plan = ChunkPlanner.Plan(body);
        Assert.Equal(1, plan.DroppedRecords);
        Assert.Empty(plan.Bodies);
    }

    [Fact]
    public void RecordsPropertyNotAnArrayIsDroppedNotThrown()
    {
        // Oversized body whose top-level shape looks right ("records" property present)
        // but is an object, not an array — unsplittable, must be a deterministic drop,
        // not an unhandled InvalidOperationException from EnumerateArray().
        var body = $$$"""{"records":{"pad":"{{{new string('x', 900_000)}}}"}}""";
        var plan = ChunkPlanner.Plan(body);
        Assert.Equal(1, plan.DroppedRecords);
        Assert.Empty(plan.Bodies);
    }
}
