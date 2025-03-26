using DuckStore.AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

//Create DataBase
var redis = builder.AddRedis("redis");
var discountDb = builder.AddSqlite("discountDb");
var orderingDb = builder.AddSqlServer("orderingDb");
var catalogDb = builder.AddPostgres("catalogDb");
var basketDb = builder.AddPostgres("basketDb");


// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalogapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(catalogDb)
    .WithReference(catalogDb)
    .WithHttpsHealthCheck("/health");

var discountapi = builder.AddProject<Projects.Discount_Grpc>(
    "discountapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(discountDb)
    .WithReference(discountDb);

var basketApi = builder.AddProject<Projects.Basket_API>(
    "basketapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(redis)
    .WaitFor(basketDb)
    .WaitFor(discountapi)
    .WithReference(redis)
    .WithReference(basketDb)
    .WithReference(discountapi)
    .WithHttpsHealthCheck("/health");
redis.WithParentRelationship(basketApi);

builder.AddProject<Projects.Ordering_API>(
    "orderapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(orderingDb)
    .WithReference(orderingDb)
    .WithHttpsHealthCheck("/health");

builder.AddProject<Projects.Ordering_MigrationService>("ordering-migration")
    .WaitFor(orderingDb)
    .WithReference(orderingDb);

// Web app
builder.AddNpmApp("shopping", "../DuckStore.WebApp.ANG")
    .WithExternalHttpEndpoints()
    .WaitFor(catalogApi)
    .WaitFor(basketApi)
    .WithReference(catalogApi)
    .WithReference(basketApi)
    .WithHttpsEndpoint(env: "PORT")
    .PublishAsDockerFile();

await builder.Build().RunAsync();

static string GetHttpForEndpoints() =>
    int.TryParse(
        Environment.GetEnvironmentVariable("ESHOP_USE_HTTP_ENDPOINTS"),
        out int result) && result == 1 ? "http" : "https";
