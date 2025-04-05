using BuildingBlocks.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Ordering.Infrastructure.Data;
using Ordering.MigrationService;

var builder = Host.CreateApplicationBuilder(args);
// Add services to the container.

if (builder.Environment.IsDevelopment())
{
    builder.AddServiceDefaults();

    builder.Services
        .AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("orderingDb"));
        })
        .AddHostedService<Worker>();
}

var host = builder.Build();
// Configure the HTTP request pipeline.

await host.RunAsync();
