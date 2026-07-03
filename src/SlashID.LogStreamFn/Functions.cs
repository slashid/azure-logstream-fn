using Microsoft.Azure.Functions.Worker;

namespace SlashID.LogStreamFn;

public class Functions
{
    private readonly Forwarder _forwarder;
    private readonly ISlashIdDelivery _delivery;

    public Functions(ISlashIdDelivery delivery)
    {
        _delivery = delivery;
        _forwarder = new Forwarder(delivery);
    }

    // MaxRetryCount -1 = retry forever; the EH checkpoint is deferred until the
    // retry policy finishes, which with -1 is "until delivery succeeds" — the
    // no-loss contract from the design spec. The 2-min delay is the
    // recovery-probe cadence during receiver outages (backpressure into the
    // Event Hub's retention window, as data, not compute).
    [Function("ForwardEvents")]
    [FixedDelayRetry(-1, "00:02:00")]
    public Task ForwardEvents(
        [EventHubTrigger("%EVENTHUB_NAME%",
            Connection = "EventHubConnection",
            ConsumerGroup = "%EVENTHUB_CONSUMER_GROUP%")] string[] messages,
        FunctionContext context)
        => _forwarder.RunAsync(messages, context.CancellationToken);

    // Heartbeat: disambiguates "quiet tenant" from "dead forwarder" for the
    // SlashID-side streaming health checks; carries the forwarder version that
    // drives upgrade-on-reconcile and the outdated-version preflight nudge.
    [Function("Heartbeat")]
    public Task Heartbeat([TimerTrigger("0 */5 * * * *")] TimerInfo _, FunctionContext context)
        => _delivery.PostBodyAsync(ControlEvents.Heartbeat(), context.CancellationToken);
}
