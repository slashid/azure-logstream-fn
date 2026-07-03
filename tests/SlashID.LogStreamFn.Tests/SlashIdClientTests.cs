using System.Net;
using SlashID.LogStreamFn;
using Xunit;

public class SlashIdClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests = new();
        public List<string> Bodies = new();
        public Queue<HttpStatusCode> Responses = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            var code = Responses.Count > 0 ? Responses.Dequeue() : HttpStatusCode.OK;
            return new HttpResponseMessage(code);
        }
    }

    private static (SlashIdClient, FakeHandler) Make()
    {
        var handler = new FakeHandler();
        var client = new SlashIdClient(
            new HttpClient(handler),
            new ForwarderConfig(
                "https://api.example.test/ip/nhi/events/v2/microsoft_tenant/azure-monitor-logs",
                "tok-123"),
            retryDelays: new[] { TimeSpan.Zero, TimeSpan.Zero }); // zero backoff in tests
        return (client, handler);
    }

    [Fact]
    public async Task PostsBodyWithAuthAndVersionHeaders()
    {
        var (client, handler) = Make();
        await client.PostBodyAsync("""{"records":[{"a":1}]}""", CancellationToken.None);

        var req = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", req.Headers.Authorization!.Parameter);
        Assert.True(req.Headers.Contains("X-SlashID-Forwarder-Version"));
        Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"records":[{"a":1}]}""", handler.Bodies[0]);
    }

    [Fact]
    public async Task RetriesTransientFailureThenSucceeds()
    {
        var (client, handler) = Make();
        handler.Responses.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.Responses.Enqueue(HttpStatusCode.OK);

        await client.PostBodyAsync("[]", CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ThrowsAfterRetryBudgetExhausted_AnyStatus()
    {
        // 401 is systemic (token misconfig) — same treatment as 5xx per spec:
        // throw; the outer retry policy holds the checkpoint; SlashID-side
        // preflight/metrics detect it.
        var (client, handler) = Make();
        handler.Responses.Enqueue(HttpStatusCode.Unauthorized);
        handler.Responses.Enqueue(HttpStatusCode.Unauthorized);
        handler.Responses.Enqueue(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<SlashIdDeliveryException>(
            () => client.PostBodyAsync("[]", CancellationToken.None));
        Assert.Equal(3, handler.Requests.Count); // initial + 2 retries (test delays array)
    }
}
