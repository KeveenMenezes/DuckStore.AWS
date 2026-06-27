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

var lambdaEmulator = builder.AddAWSLambdaServiceEmulator();

// Services
var elasticsearch = builder.AddObservability();

var orderingApi = builder.AddOrderingServices(dynamoDb, elasticsearch);

builder.AddDiscountLambdas(dynamoDb);

var basketResources = builder.AddBasketLambdas(redis, dynamoDb, lambdaEmulator);

// Reverse proxies
var yarpApiGateway = builder.AddProject<Projects.YarpApiGateway>(
    "yarp-api-gateway", GetHttpsForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(orderingApi);

// Apps
builder.AddProject<Projects.Shopping_Web_Server>(
    "shopping-web-server", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(orderingApi);

const string spaBaseUrl = "http://localhost:3000";

builder.AddNpmApp("shopping-web-spa-react", "../WebApps/Shopping.Web.SPA.React", "dev")
    .WithExternalHttpEndpoints()
    .WaitFor(yarpApiGateway)
    .WaitFor(dynamoDb)
    .WaitFor(basketResources.GetBasket)
    .WaitFor(basketResources.StoreBasket)
    .WaitFor(basketResources.DeleteBasket)
    .WaitFor(basketResources.CheckoutBasket)
    .WithReference(yarpApiGateway)
    .WithReference(dynamoDb)
    .WithReference(basketResources.GetBasket)
    .WithReference(basketResources.StoreBasket)
    .WithReference(basketResources.DeleteBasket)
    .WithReference(basketResources.CheckoutBasket)
    .WithAwsDevEnvironment()
    .WithEnvironment("CATALOG_WEBHOOK_SECRET", AppHost.Catalog.CatalogExtensions.CatalogWebhookSecretValue)
    .WithEndpoint(port: 3000, targetPort: 3000, scheme: "http", name: "http", env: "PORT", isProxied: false)
    .PublishAsDockerFile();

builder.AddCatalogLambdas(dynamoDb, spaBaseUrl);

await builder.Build().RunAsync();
return;

static string GetHttpForEndpoints() => "http";
static string GetHttpsForEndpoints() => "https";
