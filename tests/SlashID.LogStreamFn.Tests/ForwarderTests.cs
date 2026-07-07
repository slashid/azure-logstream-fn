using System.Text.Json;
using SlashID.LogStreamFn;
using Xunit;

public class ForwarderTests
{
    private sealed class FakeDelivery : ISlashIdDelivery
    {
        public List<string> Bodies = new();
        public Exception? ThrowOn;
        public Task PostBodyAsync(string jsonBody, CancellationToken ct)
        {
            if (ThrowOn is not null) throw ThrowOn;
            lock (Bodies) Bodies.Add(jsonBody);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ForwardsEachMessageBody()
    {
        // Records from every message in the batch are pooled together and
        // packed into as few bodies as possible, so these two small messages
        // (3 records total) coalesce into a single POST.
        var fake = new FakeDelivery();
        await new Forwarder(fake).RunAsync(new[]
        {
            """{"records":[{"a":1},{"a":2}]}""",
            """{"records":[{"a":3}]}""",
        }, CancellationToken.None);

        Assert.Single(fake.Bodies);
        var total = fake.Bodies.Sum(b =>
            JsonDocument.Parse(b).RootElement.GetProperty("records").GetArrayLength());
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task DeliveryFailurePropagates() // → outer retry policy holds checkpoint
    {
        var fake = new FakeDelivery { ThrowOn = new SlashIdDeliveryException("down") };
        await Assert.ThrowsAsync<SlashIdDeliveryException>(() =>
            new Forwarder(fake).RunAsync(new[] { """{"records":[{"a":1}]}""" }, CancellationToken.None));
    }

    [Fact]
    public async Task DroppedRecordsEmitControlEventAndDeliveryContinues()
    {
        var fake = new FakeDelivery();
        var big = new string('x', 900_000);
        await new Forwarder(fake).RunAsync(new[]
        {
            $$"""{"records":[{"a":1},{"pad":"{{big}}"},{"a":2}]}""",
        }, CancellationToken.None);

        var controlBodies = fake.Bodies.Where(b => b.Contains(ControlEvents.RecordDroppedType)).ToList();
        var control = Assert.Single(controlBodies);
        using var doc = JsonDocument.Parse(control);
        Assert.Equal(1, doc.RootElement[0].GetProperty("data").GetProperty("droppedRecords").GetInt32());

        var dataRecords = fake.Bodies.Except(controlBodies).Sum(b =>
            JsonDocument.Parse(b).RootElement.GetProperty("records").GetArrayLength());
        Assert.Equal(2, dataRecords);
    }

    [Fact]
    public void ForwardEventsHasInfiniteFixedDelayRetry()
    {
        var method = typeof(Functions).GetMethod("ForwardEvents")!;
        var attr = (Microsoft.Azure.Functions.Worker.FixedDelayRetryAttribute)
            method.GetCustomAttributes(typeof(Microsoft.Azure.Functions.Worker.FixedDelayRetryAttribute), false)
            .Single();
        Assert.Equal(-1, attr.MaxRetryCount);
    }

    [Fact]
    public async Task CoalescesBurstOfSmallMessagesIntoOnePost()
    {
        var fake = new FakeDelivery();
        var msgs = Enumerable.Range(0, 500).Select(_ => """{"records":[{"a":1}]}""").ToArray();
        await new Forwarder(fake).RunAsync(msgs, CancellationToken.None);

        var body = Assert.Single(fake.Bodies);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(500, doc.RootElement.GetProperty("records").GetArrayLength());
    }

    private static string BigRecordsMsg(int seq) =>
        $$"""{"records":[{"seq":{{seq}},"pad":"{{new string('x', 700_000)}}"}]}""";

    private sealed class ConcurrencyProbe : ISlashIdDelivery
    {
        private int _inFlight;
        public int MaxObserved;
        public async Task PostBodyAsync(string jsonBody, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _inFlight);
            int prev;
            do { prev = MaxObserved; if (now <= prev) break; } while (Interlocked.CompareExchange(ref MaxObserved, now, prev) != prev);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref _inFlight);
        }
    }

    [Fact]
    public async Task RespectsMaxConcurrentDeliveriesBound()
    {
        var probe = new ConcurrencyProbe();
        // 10 messages, each with one ~700 KB record: two can't share a 768 KB
        // body, so PlanBatch yields exactly one body per message.
        var msgs = Enumerable.Range(0, 10).Select(BigRecordsMsg).ToArray();
        await new Forwarder(probe, maxConcurrentDeliveries: 2).RunAsync(msgs, CancellationToken.None);

        Assert.Equal(2, probe.MaxObserved);
    }

    private sealed class FailOnNthDelivery : ISlashIdDelivery
    {
        private readonly int _failAt;
        private int _n;
        public FailOnNthDelivery(int failAt) => _failAt = failAt;
        public Task PostBodyAsync(string jsonBody, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _n) == _failAt) throw new SlashIdDeliveryException("boom");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task OnePermanentFailureAmongManyBodiesStillThrows()
    {
        var fake = new FailOnNthDelivery(failAt: 3);
        var msgs = Enumerable.Range(0, 5).Select(BigRecordsMsg).ToArray();
        await Assert.ThrowsAsync<SlashIdDeliveryException>(() =>
            new Forwarder(fake, maxConcurrentDeliveries: 4).RunAsync(msgs, CancellationToken.None));
    }
}
