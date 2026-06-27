using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using Catalog.DevelopmentDataSeeder;
using Catalog.Function.Data;
using Catalog.Function.Repositories;

var builder = Host.CreateApplicationBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.AddServiceDefaults();

    builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
    builder.Services.AddSingleton<IProductRepository, DynamoProductRepository>();
    builder.Services.AddSingleton<ICategoryRepository, DynamoCategoryRepository>();
    builder.Services.AddScoped<CatalogInitialData>();
    builder.Services.AddHostedService<Worker>();
}

await builder.Build().RunAsync();
