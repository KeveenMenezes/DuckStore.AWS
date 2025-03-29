using BuildingBlocks.ServiceDefaults;
using Ordering.Application;
using Ordering.Infrastructure;
using Ordering.MigrationService;

var builder = Host.CreateApplicationBuilder(args);
// Add services to the container.

if (builder.Environment.IsDevelopment())
{
    builder.AddServiceDefaults();

    builder.Services
        .AddApplicationServices()
        .AddInfrastructureServices(builder.Configuration)
        .AddHostedService<Worker>();
}

var host = builder.Build();
// Configure the HTTP request pipeline.

await host.RunAsync();
