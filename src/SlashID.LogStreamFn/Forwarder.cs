namespace SlashID.LogStreamFn;

public class Forwarder
{
    private readonly ISlashIdDelivery _delivery;

    public Forwarder(ISlashIdDelivery delivery) => _delivery = delivery;

    public async Task RunAsync(string[] messages, CancellationToken ct)
    {
        var totalDropped = 0;
        foreach (var message in messages)
        {
            var plan = ChunkPlanner.Plan(message); // malformed JSON throws → checkpoint held
            totalDropped += plan.DroppedRecords;
            foreach (var body in plan.Bodies)
                await _delivery.PostBodyAsync(body, ct);
        }

        if (totalDropped > 0)
            await _delivery.PostBodyAsync(
                ControlEvents.RecordDropped(totalDropped, "record exceeds receiver body cap"), ct);
    }
}
