var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

builder.AddNpgsqlDataSource("catalogDb");

var assembly = typeof(Program).Assembly;
builder.Services
    .AddCors(options =>
    {
        options.AddPolicy("AllowAngularApp",
            policy =>
            {
                policy.WithOrigins("https://localhost:4200")
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
    })
    .AddExceptionHandler<CustomExceptionHandler>()
    .AddCarter()
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(assembly);
        config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(LoggingBehavior<,>));
    })
    .AddValidatorsFromAssembly(assembly)
    .AddMarten(opts => { })
    .UseLightweightSessions()
    .UseNpgsqlDataSource();

if (builder.Environment.IsDevelopment())
{
    builder.Services.InitializeMartenWith<CatagoInitialData>();
}
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("catalogDb")!);

var app = builder.Build();
// Configure the HTTP request pipeline.

app.UsePathBase("/api");
app.UseCors("AllowAngularApp");
app.MapCarter();
app.UseExceptionHandler(options => { });
app.UseHealthChecks(
    "/health",
    new HealthCheckOptions
    {
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
    });

await app.RunAsync();
