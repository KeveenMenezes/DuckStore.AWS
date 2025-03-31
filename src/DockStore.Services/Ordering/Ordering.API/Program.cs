using Ordering.Application.Configuration;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

builder.Services
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration);

var app = builder.Build();
// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

await app.RunAsync();
