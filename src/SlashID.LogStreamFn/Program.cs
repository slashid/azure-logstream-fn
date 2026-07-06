using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SlashID.LogStreamFn;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton(new ForwarderConfig(
            EventsEndpoint: Environment.GetEnvironmentVariable("SLASHID_EVENTS_ENDPOINT")
                ?? throw new InvalidOperationException("SLASHID_EVENTS_ENDPOINT is required"),
            PushAuthToken: Environment.GetEnvironmentVariable("SLASHID_PUSH_AUTH_TOKEN")
                ?? throw new InvalidOperationException("SLASHID_PUSH_AUTH_TOKEN is required"),
            MaxConcurrentDeliveries:
                int.TryParse(Environment.GetEnvironmentVariable("MAX_CONCURRENT_DELIVERIES"), out var mcd) && mcd > 0
                    ? mcd
                    : 4));
        services.AddHttpClient<SlashIdClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            });
        services.AddSingleton<ISlashIdDelivery>(sp => sp.GetRequiredService<SlashIdClient>());
    })
    .Build();

host.Run();
