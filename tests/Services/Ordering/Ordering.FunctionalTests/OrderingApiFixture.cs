using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Ordering.FunctionalTests;

public sealed class OrderingApiFixture
    : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IHost _app;

    private readonly IResourceBuilder<SqlServerServerResource> _orderingDb;
    private readonly IResourceBuilder<RabbitMQServerResource> _rabbitMq;
    private string _orderingDbConnectionString;
    private string _rabbitMqConnectionString;

    public OrderingApiFixture()
    {
        var options = new DistributedApplicationOptions
        {
            AssemblyName = typeof(OrderingApiFixture).Assembly.FullName,
            DisableDashboard = true
        };

        var appBuilder = DistributedApplication.CreateBuilder(options);

        _orderingDb = appBuilder.AddSqlServer("OrderingDb");
        _rabbitMq = appBuilder.AddRabbitMQ("RabbitMq");
        _app = appBuilder.Build();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:orderingDb", _orderingDbConnectionString },
                { "MessageBroker:Host", _rabbitMqConnectionString },
                { "MessageBroker:UserName", "guest" },
                { "MessageBroker:Password", "guest" },
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
        
        // Get RabbitMQ connection string using the amqp format
        var rabbitMqEndpoint = _rabbitMq.Resource.PrimaryEndpoint;
        var host = rabbitMqEndpoint.Host;
        var port = rabbitMqEndpoint.Port;
        _rabbitMqConnectionString = $"amqp://{host}:{port}";
    }
}
