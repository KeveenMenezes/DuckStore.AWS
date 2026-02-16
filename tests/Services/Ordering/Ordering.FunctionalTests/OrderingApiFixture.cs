using Microsoft.Extensions.Logging;

namespace Ordering.FunctionalTests;

public sealed class OrderingApiFixture
    : IAsyncLifetime
{
    /// <summary>
    /// Timeout for the full application startup including Docker containers
    /// (SQL Server, RabbitMQ, Elasticsearch JVM, migrations, etc.).
    /// </summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    private DistributedApplication _app;

    public HttpClient HttpClient { get; private set; }

    public Task DisposeAsync()
    {
        HttpClient.Dispose();
        _app.Dispose();

        return Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AppHost>(["DcpPublisher:RandomizePorts=false"]);

        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        _app = await appHost.BuildAsync(cts.Token);

        await _app.StartAsync(cts.Token);

        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("ordering-api", cts.Token);

        HttpClient = _app.CreateHttpClient("ordering-api");
    }
}
