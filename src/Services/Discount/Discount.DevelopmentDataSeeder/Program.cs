using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using Discount.DevelopmentDataSeeder;
using Discount.Function.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddSingleton<ICouponRepository, DynamoCouponRepository>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
