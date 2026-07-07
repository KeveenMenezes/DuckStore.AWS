using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using Pricing.DevelopmentDataSeeder;
using Pricing.Function.Modules.Campaigns.Data;
using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.Prices.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddScoped<IPriceRepository, DynamoPriceRepository>();
builder.Services.AddScoped<ICampaignRepository, DynamoCampaignRepository>();
builder.Services.AddScoped<IGatewayCostRepository, DynamoGatewayCostRepository>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
