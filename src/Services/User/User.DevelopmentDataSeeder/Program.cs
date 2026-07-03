using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using User.DevelopmentDataSeeder;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
