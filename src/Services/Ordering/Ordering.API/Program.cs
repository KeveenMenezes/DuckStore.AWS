var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddRabbitMQClient("messageBroker");
builder.AddServiceDefaults();

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
