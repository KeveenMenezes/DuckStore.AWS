using AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// DataBase
var redis = builder.AddRedis("redis");
var discountDb = builder.AddSqlite("discountDb");
var catalogDb = builder.AddPostgres("catalogDb");
var basketDb = builder.AddPostgres("basketDb");
var orderingDb = builder.AddSqlServer("orderingDb");

// Messaging
var rabbitmq = builder
    .AddRabbitMQ(
        "messageBroker",
        builder.AddParameter("username", secret: true),
        builder.AddParameter("password", secret: true),
        5672)
    .WithManagementPlugin();

// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalog-api", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(catalogDb)
    .WaitFor(rabbitmq)
    .WithReference(catalogDb)
    .WithReference(rabbitmq)
    .WithHttpsHealthCheck("/health");

var discountApi = builder.AddProject<Projects.Discount_Grpc>(
    "discount-api", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(discountDb)
    .WithReference(discountDb);

var basketApi = builder.AddProject<Projects.Basket_API>(
    "basket-api", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(redis)
    .WaitFor(basketDb)
    .WaitFor(discountApi)
    .WaitFor(catalogApi)
    .WaitFor(rabbitmq)
    .WithReference(redis)
    .WithReference(basketDb)
    .WithReference(discountApi)
    .WithReference(catalogApi)
    .WithReference(rabbitmq)
    .WithHttpsHealthCheck("/health");
redis.WithParentRelationship(basketApi);

var orderingMigration = builder.AddProject<Projects.Ordering_MigrationService>("ordering-migration")
    .WaitFor(orderingDb)
    .WithReference(orderingDb);

builder.AddProject<Projects.Ordering_API>(
    "ordering-api", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WaitFor(orderingDb)
    .WithReference(orderingMigration)
    .WithReference(orderingDb)
    .WithHttpsHealthCheck("/health");

// Web app
builder.AddNpmApp("shopping-web", "../WebApps/WebApp.ANG")
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
