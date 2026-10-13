#pragma warning disable CA2252 // Opt in to preview features
using AppHost.Basket;
using AppHost.Catalog;
using AppHost.CatalogView;
using AppHost.Challenges;
using AppHost.Extensions;
using AppHost.Inventory;
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

builder.AddChallengesServices(dynamoDb);

var inventoryResources = builder.AddInventoryServices(dynamoDb);

// Apps
builder.AddNpmApp("shopping-web-spa-react", "../WebApps/Shopping.Web.SPA.React", "dev")
    .WithExternalHttpEndpoints()
    .WaitFor(dynamoDb)
    .WaitFor(basketResources.CheckoutBasket)
    .WaitFor(basketResources.MergeBasket)
    .WaitFor(userResources)
    // local.ts serves the warehouse fields straight from DynamoDB Local and the stock movements
    // through the Inventory Lambdas (ADR-0052 §5, §7).
    .WaitForCompletion(inventoryResources.Seeder)
    .WaitFor(inventoryResources.MovementLambdas[0])
    .WaitFor(inventoryResources.MovementLambdas[1])
    .WaitFor(inventoryResources.MovementLambdas[2])
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

// Blazor WASM can't read env vars at runtime — its GraphQL endpoint comes from the
// checked-in wwwroot/appsettings.Development.json, pointing at the React SPA's local
// GraphQL BFF (start the SPA first). Deployed as a static site to S3/CloudFront.
builder.AddProject<Projects.Managment_Web_Blazor>("managment-web-blazor")
    .WithExternalHttpEndpoints()
    .WithExplicitStart();

await builder.Build().RunAsync();
