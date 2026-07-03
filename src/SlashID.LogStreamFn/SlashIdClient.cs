namespace SlashID.LogStreamFn;

public interface ISlashIdDelivery
{
    Task PostBodyAsync(string jsonBody, CancellationToken ct);
}

public class SlashIdClient : ISlashIdDelivery
{
    public SlashIdClient(HttpClient http, ForwarderConfig config) { }
    public Task PostBodyAsync(string jsonBody, CancellationToken ct) => Task.CompletedTask;
}
