using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using Catalog.DevelopmentDataSeeder;
using Catalog.Function.Modules.Categories.Data;
using Catalog.Function.Modules.Products.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddSingleton<IProductRepository, DynamoProductRepository>();
builder.Services.AddSingleton<ICategoryRepository, DynamoCategoryRepository>();
builder.Services.AddScoped<CatalogInitialData>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
