using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults.Behaviors;
using BuildingBlocks.ServiceDefaults.ExceptionHandler;
using Catalog.API.Repositories;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();
builder.AddElasticsearch();

var assembly = typeof(Program).Assembly;
builder.Services
    .AddExceptionHandler<CustomExceptionHandler>()
    .AddCarter()
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(assembly);
        config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(LoggingBehavior<,>));
    })
    .AddValidatorsFromAssembly(assembly);

//Injection dependence — DynamoDB Local injeta AWS_ENDPOINT_URL_DYNAMODB; o SDK resolve sozinho.
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

builder.Services.AddScoped<IProductRepository, DynamoProductRepository>();
builder.Services.AddScoped<ICategoryRepository, DynamoCategoryRepository>();
builder.Services.AddScoped<CatalogInitialData>();

var app = builder.Build();
// Configure the HTTP request pipeline.

await app.Services.EnsureCatalogTablesCreatedAsync();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CatalogInitialData>().PopulateAsync();
}

app.MapDefaultEndpoints();
app.MapCarter();
app.UseExceptionHandler(options => { });

await app.RunAsync();
