using BuildingBlocks.Core.Abstractions;
using BuildingBlocks.ServiceDefaults.Behaviors;
using BuildingBlocks.ServiceDefaults.ExceptionHandler;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("catalogDb");

var assembly = typeof(Program).Assembly;
builder.Services
    .AddExceptionHandler<CustomExceptionHandler>()
    .AddCarter()
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(assembly);
        config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(LoggingBehavior<,>));
        config.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
    })
    .AddValidatorsFromAssembly(assembly)
    .AddMarten(opts =>
    {
        opts.UseSystemTextJsonForSerialization(configure: jsonOptions =>
        {
            jsonOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        });
    })
    .UseLightweightSessions()
    .UseNpgsqlDataSource();

//Injection dependence
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.InitializeMartenWith<CatalogInitialData>();
}

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("catalogDb")!);

var app = builder.Build();
// Configure the HTTP request pipeline.

app.MapDefaultEndpoints();
app.MapCarter();
app.UseExceptionHandler(options => { });

await app.RunAsync();
