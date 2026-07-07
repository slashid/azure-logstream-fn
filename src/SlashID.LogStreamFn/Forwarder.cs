namespace SlashID.LogStreamFn;

public class Forwarder
{
    private readonly ISlashIdDelivery _delivery;
    private readonly int _maxConcurrentDeliveries;

    public Forwarder(ISlashIdDelivery delivery, int maxConcurrentDeliveries = 4)
    {
        _delivery = delivery;
        _maxConcurrentDeliveries = maxConcurrentDeliveries;
    }

    public async Task RunAsync(string[] messages, CancellationToken ct)
    {
        // Parse+plan the WHOLE batch first: malformed JSON throws before any POST,
        // so a bad message can't cause a partial send. Records from every message
        // are pooled into as few ≤MaxChunkBytes bodies as possible (a burst of
        // small messages becomes one POST). The verbatim fast path is intentionally
        // given up for merged bodies — records are re-serialized; the coalescing
        // win dominates.
        var plan = ChunkPlanner.PlanBatch(messages);

        // Deliver bodies concurrently with a bounded degree of parallelism so we
        // don't hammer the receiver. No-loss contract: if ANY body permanently
        // fails after its inner retry budget, Parallel.ForEachAsync cancels the
        // siblings and rethrows out of RunAsync → the outer FixedDelayRetry(-1)
        // holds the Event Hub checkpoint for redelivery. Partial success is fine
        // (duplicates acceptable under at-least-once).
        await Parallel.ForEachAsync(plan.Bodies, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _maxConcurrentDeliveries),
            CancellationToken = ct,
        }, async (body, token) => await _delivery.PostBodyAsync(body, token));

        if (plan.DroppedRecords > 0)
            await _delivery.PostBodyAsync(
                ControlEvents.RecordDropped(plan.DroppedRecords, "record exceeds receiver body cap"), ct);
    }
}
