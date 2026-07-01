#pragma warning disable CA2252 // Opt in to preview features
using AppHost.Basket;
using AppHost.Catalog;
using AppHost.Extensions;
using AppHost.Observability;
using AppHost.Ordering;
using AppHost.Review;
using Aspire.Hosting.AWS.DynamoDB;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

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

builder.AddOrderingServices(dynamoDb, elasticsearch);

var basketResources = builder.AddBasketLambdas(dynamoDb);

builder.AddReviewServices(dynamoDb);

// Reverse proxies
var yarpApiGateway = builder.AddProject<Projects.YarpApiGateway>(
    "yarp-api-gateway", GetHttpsForEndpoints())
    .WithExternalHttpEndpoints();

// Apps
builder.AddProject<Projects.Shopping_Web_Server>(
    "shopping-web-server", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithExplicitStart();

const string spaBaseUrl = "http://localhost:3000";

builder.AddNpmApp("shopping-web-spa-react", "../WebApps/Shopping.Web.SPA.React", "dev")
    .WithExternalHttpEndpoints()
    .WaitFor(yarpApiGateway)
    .WaitFor(dynamoDb)
    .WaitFor(basketResources.StoreBasket)
    .WaitFor(basketResources.CheckoutBasket)
    .WithReference(yarpApiGateway)
    .WithReference(dynamoDb)
    .WithEnvironment(ctx =>
    {
        if (!ctx.ExecutionContext.IsPublishMode)
            ctx.EnvironmentVariables["AWS_ENDPOINT_URL_LAMBDA"] = lambdaEmulator.GetEndpoint("http");
    })
    .WithAwsDevEnvironment()
    .WithEnvironment("CATALOG_WEBHOOK_SECRET", AppHost.Catalog.CatalogExtensions.CatalogWebhookSecretValue)
    .WithEndpoint(port: 3000, targetPort: 3000, scheme: "http", name: "http", env: "PORT", isProxied: false)
    .PublishAsDockerFile()
    .WithExplicitStart();

builder.AddCatalogLambdas(dynamoDb, spaBaseUrl);

await builder.Build().RunAsync();
return;

static string GetHttpForEndpoints() => "http";
static string GetHttpsForEndpoints() => "https";
