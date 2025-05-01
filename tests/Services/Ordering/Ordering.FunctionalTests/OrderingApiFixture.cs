namespace Ordering.FunctionalTests;

public sealed class OrderingApiFixture : IAsyncLifetime
{
    private DistributedApplication _app;
    public HttpClient HttpClient => _app.CreateHttpClient("ordering-api");

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Ordering_API>(
            [
                "--environment=Testing"
            ],
            configureBuilder: (appOptions, hostSettings) =>
            {
                appOptions.DisableDashboard = true;
            });

        _app = await appHost.BuildAsync();

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app != null)
            await _app
                .DisposeAsync()
                .ConfigureAwait(false);
    }
}
