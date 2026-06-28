using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using Ordering.DevelopmentDataSeeder;
using Ordering.Function.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddScoped<IOrderRepository, DynamoOrderRepository>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
