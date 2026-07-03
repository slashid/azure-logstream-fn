using System.Net.Http.Headers;
using System.Text;

namespace SlashID.LogStreamFn;

public interface ISlashIdDelivery
{
    Task PostBodyAsync(string jsonBody, CancellationToken ct);
}

public class SlashIdDeliveryException : Exception
{
    public SlashIdDeliveryException(string msg, Exception? inner = null) : base(msg, inner) { }
}

public class SlashIdClient : ISlashIdDelivery
{
    // 1s,2s,4s,8s between 5 attempts; with the 10s HTTP timeout the worst case
    // is ~65s per body — far below any functionTimeout (10 min on the Y1
    // fallback plan), so the host never kills us mid-retry.
    private static readonly TimeSpan[] DefaultDelays =
    {
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
    };

    private readonly HttpClient _http;
    private readonly ForwarderConfig _config;
    private readonly TimeSpan[] _retryDelays;

    public SlashIdClient(HttpClient http, ForwarderConfig config, TimeSpan[]? retryDelays = null)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _config = config;
        _retryDelays = retryDelays ?? DefaultDelays;
    }

    /// <summary>POSTs one JSON body. Retries any failure (status or network)
    /// with bounded backoff, then throws — the caller's retry policy owns
    /// durability. All HTTP failures are treated identically: the receiver's
    /// deterministic 4xx causes are systemic (401 token misconfig, 400/413
    /// forwarder bug), detected SlashID-side.</summary>
    public async Task PostBodyAsync(string jsonBody, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= _retryDelays.Length; attempt++)
        {
            if (attempt > 0) await Task.Delay(_retryDelays[attempt - 1], ct);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, _config.EventsEndpoint)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.PushAuthToken);
                req.Headers.Add("X-SlashID-Forwarder-Version", ForwarderVersion.Current);

                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) return;
                last = new SlashIdDeliveryException(
                    $"SlashID returned {(int)resp.StatusCode} on attempt {attempt + 1}");
            }
            // Only treat TaskCanceledException as a retryable transient failure when it
            // did NOT originate from the caller's own token (e.g. an HttpClient-internal
            // send timeout instead). A genuine caller-driven cancellation must propagate
            // as OperationCanceledException, not be swallowed into a retry/delivery
            // exception — the caller (and its checkpoint semantics) needs to see it.
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                last = ex;
            }
        }
        throw last as SlashIdDeliveryException
            ?? new SlashIdDeliveryException("delivery failed", last);
    }
}
