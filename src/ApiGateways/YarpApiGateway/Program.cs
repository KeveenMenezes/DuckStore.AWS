using BuildingBlocks.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;
using YarpApiGateway;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

builder.Services
    .AddApplicationServices()
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.AddFixedWindowLimiter("fixed", options =>
    {
        options.Window = TimeSpan.FromSeconds(10);
        options.PermitLimit = 5;
    });
});

var app = builder.Build();
// Configure the HTTP request pipeline.

app.MapDefaultEndpoints();
app.UseRateLimiter();
app.MapReverseProxy();

await app.RunAsync();