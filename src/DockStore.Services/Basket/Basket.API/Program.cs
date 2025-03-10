var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

var assembly = typeof(Program).Assembly;
builder.Services
    .AddCarter()
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(assembly);
        config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(LoggingBehavior<,>));
    })
    .AddValidatorsFromAssembly(assembly);

var app = builder.Build();
// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UsePathBase("/api");
app.MapCarter();
app.UseExceptionHandler(options => { });

await app.RunAsync();
