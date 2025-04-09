using AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// DataBase
var redis = builder.AddRedis("redis");
var discountDb = builder.AddSqlite("discountDb");
var catalogDb = builder.AddPostgres("catalogDb");
var basketDb = builder.AddPostgres("basketDb");
var orderingDb = builder.AddSqlServer("orderingDb");

var orderingMigration = builder.AddProject<Projects.Ordering_MigrationService>("ordering-migration")
    .WaitFor(orderingDb)
    .WithReference(orderingDb);

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
    "catalog-api")
    .WaitFor(catalogDb)
    .WaitFor(rabbitmq)
    .WithReference(catalogDb)
    .WithReference(rabbitmq)
    .WithHttpsHealthCheck("/health");

var discountApi = builder.AddProject<Projects.Discount_Grpc>(
    "discount-api", GetHttpForEndpoints())
    .WaitFor(discountDb)
    .WithReference(discountDb);

var basketApi = builder.AddProject<Projects.Basket_API>(
    "basket-api")
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

var orderingApi = builder.AddProject<Projects.Ordering_API>(
    "ordering-api")
    .WaitFor(orderingDb)
    .WithReference(orderingMigration)
    .WithReference(orderingDb)
    .WithHttpsHealthCheck("/health");

// Reverse proxies
builder.AddProject<Projects.YarpApiGateway>(
    "yarp-api-gateway", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WithReference(basketApi);

// Apps
builder.AddNpmApp("shopping-web", "../WebApps/WebApp.ANG")
    .WithExternalHttpEndpoints()
    .WaitFor(catalogApi)
    .WaitFor(basketApi)
    .WithReference(catalogApi)
    .WithReference(basketApi)
    .WithHttpsEndpoint(env: "PORT")
    .PublishAsDockerFile();

await builder.Build().RunAsync();

static string GetHttpForEndpoints() => "https";
