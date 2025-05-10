using BuildingBlocks.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

builder.Services
    .AddRateLimiter(rateLimiterOptions =>
    {
        rateLimiterOptions.AddFixedWindowLimiter(
            "fixed",
            options =>
            {
                options.Window = TimeSpan.FromSeconds(10);
                options.PermitLimit = 5;
            });
    })
    .AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:4200")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    })
    .AddReverseProxy()

    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();


var app = builder.Build();
// Configure the HTTP request pipeline.

app.UseCors();
app.MapDefaultEndpoints();
app.UseRateLimiter();
app.MapReverseProxy();

await app.RunAsync();
