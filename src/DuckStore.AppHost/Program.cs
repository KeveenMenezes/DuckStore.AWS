using DuckStore.AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// DataBase
var redis = builder.AddRedis("redis");
var discountDb = builder.AddSqlite("discountDb");
var catalogDb = builder.AddPostgres("catalogDb");
var basketDb = builder.AddPostgres("basketDb");
var orderingDb = builder.AddSqlServer("orderingDb");

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
    .WaitFor(catalogApi)
    .WithReference(redis)
    .WithReference(basketDb)
    .WithReference(discountapi)
    .WithReference(catalogApi)
    .WithHttpsHealthCheck("/health");
redis.WithParentRelationship(basketApi);

var orderingMigration = builder.AddProject<Projects.Ordering_MigrationService>("ordering-migration")
    .WaitFor(orderingDb)
    .WithReference(orderingDb);

builder.AddProject<Projects.Ordering_API>(
    "orderapi", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(orderingDb)
    .WithReference(orderingMigration)
    .WithReference(orderingDb)
    .WithHttpsHealthCheck("/health");

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
