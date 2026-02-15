using BuildingBlocks.ServiceDefaults.ExceptionHandler;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();
builder.AddElasticsearch();
builder.AddRabbitMQClient("messageBroker");

builder.Services
    .AddCarter()
    .AddExceptionHandler<CustomExceptionHandler>()
    .AddApplicationServices(builder.Configuration)
    .AddInfrastructureServices(builder.Configuration);

var app = builder.Build();
// Configure the HTTP request pipeline.

app.MapDefaultEndpoints();
app.MapCarter();
app.UseExceptionHandler(options => { });

await app.RunAsync();
