namespace SlashID.LogStreamFn;

public record ForwarderConfig(string EventsEndpoint, string PushAuthToken, int MaxConcurrentDeliveries = 4);
