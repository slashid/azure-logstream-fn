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
            Bodies.Add(jsonBody);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ForwardsEachMessageBody()
    {
        var fake = new FakeDelivery();
        await new Forwarder(fake).RunAsync(new[]
        {
            """{"records":[{"a":1},{"a":2}]}""",
            """{"records":[{"a":3}]}""",
        }, CancellationToken.None);

        Assert.Equal(2, fake.Bodies.Count); // one small message = one verbatim POST
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
}
