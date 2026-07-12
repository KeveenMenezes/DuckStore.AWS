#pragma warning disable CA2252 // Opt in to preview features
using AppHost.Basket;
using AppHost.Catalog;
using AppHost.CatalogView;
using AppHost.Extensions;
using AppHost.Observability;
using AppHost.Ordering;
using AppHost.Payment;
using AppHost.Pricing;
using AppHost.Review;
using AppHost.User;
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

builder.AddPaymentServices(dynamoDb);

var pricingSeeder = builder.AddPricingServices(dynamoDb);

var basketResources = builder.AddBasketLambdas(dynamoDb);

var userResources = builder.AddUserResources(dynamoDb);

var reviewSeeder = builder.AddReviewServices(dynamoDb);

var catalogSeeder = builder.AddCatalogLambdas(dynamoDb);

builder.AddCatalogViewLambdas(dynamoDb, catalogSeeder, reviewSeeder, pricingSeeder);

// Apps
builder.AddNpmApp("shopping-web-spa-react", "../WebApps/Shopping.Web.SPA.React", "dev")
    .WithExternalHttpEndpoints()
    .WaitFor(dynamoDb)
    .WaitFor(basketResources.CheckoutBasket)
    .WaitFor(basketResources.MergeBasket)
    .WaitFor(userResources)
    .WithReference(dynamoDb)
    .WithEnvironment(ctx =>
    {
        if (!ctx.ExecutionContext.IsPublishMode)
            ctx.EnvironmentVariables["AWS_ENDPOINT_URL_LAMBDA"] = lambdaEmulator.GetEndpoint("http");
    })
    .WithAwsDevEnvironment()
    .WithEndpoint(port: 3000, targetPort: 3000, scheme: "http", name: "http", env: "PORT", isProxied: false)
    .PublishAsDockerFile()
    .WithExplicitStart();

await builder.Build().RunAsync();
