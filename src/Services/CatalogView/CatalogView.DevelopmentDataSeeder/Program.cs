using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using CatalogView.DevelopmentDataSeeder;
using CatalogView.Function.Modules.Products.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

builder.Services.AddSingleton<IProductSearchIndex, DynamoProductIndex>();
builder.Services.AddScoped<ProductBackfill>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
