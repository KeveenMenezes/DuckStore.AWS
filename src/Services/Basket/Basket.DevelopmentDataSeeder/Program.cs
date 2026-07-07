using Amazon.DynamoDBv2;
using Basket.DevelopmentDataSeeder;
using Basket.Function.Modules.ShoppingCarts.Data;
using BuildingBlocks.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
