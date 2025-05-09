using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Ordering.FunctionalTests;

public sealed class OrderingApiFixture
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IHost _app;

    private readonly IResourceBuilder<SqlServerServerResource> _orderingDb;
    private string _orderingDbConnectionString;

    public OrderingApiFixture()
    {
        var options = new DistributedApplicationOptions
        {
            AssemblyName = typeof(OrderingApiFixture).Assembly.FullName,
            DisableDashboard = true
        };

        var appBuilder = DistributedApplication.CreateBuilder(options);

        _orderingDb = appBuilder.AddSqlServer("OrderingDb");
        appBuilder.AddRabbitMQ("RabbitMq");
        _app = appBuilder.Build();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { $"ConnectionStrings:{_orderingDb.Resource.Name}", _orderingDbConnectionString },
            });
        });

        return base.CreateHost(builder);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _app.StopAsync();
        if (_app is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _app.Dispose();
        }
    }

    public async Task InitializeAsync()
    {
        await _app.StartAsync();
        _orderingDbConnectionString = await _orderingDb.Resource.GetConnectionStringAsync() ??
            throw new InvalidOperationException("Could not get connection string for OrderingDb");
    }
}
