using BuildingBlocks.ServiceDefaults;
using Ordering.Infrastructure.Data;
using Ordering.MigrationService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHostedService<Worker>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

builder.AddSqlServerDbContext<ApplicationDbContext>("orderingDb");

var host = builder.Build();
await host.RunAsync();