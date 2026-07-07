using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using CatalogView.DevelopmentDataSeeder;
using CatalogView.Function.Modules.Products.Data;
using OpenSearch.Net;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());

builder.Services.AddSingleton<IOpenSearchLowLevelClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var endpoint = configuration["OpenSearch:Endpoint"] ?? "http://localhost:9200";
    var pool = new SingleNodeConnectionPool(new Uri(endpoint));
    return new OpenSearchLowLevelClient(new ConnectionConfiguration(pool));
});

builder.Services.AddSingleton<IProductSearchIndex, OpenSearchProductIndex>();
builder.Services.AddScoped<ProductBackfill>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
