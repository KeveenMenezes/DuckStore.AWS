#pragma warning disable CA2252 // Opt in to preview features
using AppHost.Basket;
using AppHost.Catalog;
using AppHost.Discount;
using AppHost.Extensions;
using AppHost.Observability;
using AppHost.Ordering;
using Aspire.Hosting.AWS.DynamoDB;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// Cache
var redis = builder.AddRedis("redis");

// Data Base
var dynamoDb = builder.
    AddAWSDynamoDBLocal("dynamo", new DynamoDBLocalOptions
    {
        SharedDb = true
    })
    .WithHttpEndpoint(8000, 8000);

builder.AddAWSLambdaServiceEmulator();

// Services
var elasticsearch = builder.AddObservability();

var orderingApi = builder.AddOrderingServices(dynamoDb, elasticsearch);

var discountApi = builder.AddDiscountApi(dynamoDb, elasticsearch);

var basketApi = builder.AddBasketApi(redis, dynamoDb, discountApi, elasticsearch);

builder.AddCatalogLambdas(dynamoDb);

// Reverse proxies
var yarpApiGateway = builder.AddProject<Projects.YarpApiGateway>(
    "yarp-api-gateway", GetHttpsForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(orderingApi)
    .WithReference(basketApi);

// Apps
builder.AddProject<Projects.Shopping_Web_Server>(
    "shopping-web-server", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(basketApi)
    .WithReference(orderingApi);

builder.AddNpmApp("shopping-web-spa", "../WebApps/Shopping.Web.SPA")
    .WithExternalHttpEndpoints()
    .WaitFor(yarpApiGateway)
    .WithReference(yarpApiGateway)
    .WithEndpoint(port: 4200, targetPort: 4200, scheme: "https", name: "https", env: "PORT", isProxied: false)
    .PublishAsDockerFile();

await builder.Build().RunAsync();
return;

static string GetHttpForEndpoints() => "http";
static string GetHttpsForEndpoints() => "https";
