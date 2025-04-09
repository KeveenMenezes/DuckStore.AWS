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
//TODO: incluir a conexão nas suas refenencias.
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
    .WaitFor(rabbitmq)
    .WithReference(redis)
    .WithReference(basketDb)
    .WithReference(discountApi)
    .WithReference(rabbitmq)
    .WithHttpsHealthCheck("/health");
redis.WithParentRelationship(basketApi);

var orderingApi = builder.AddProject<Projects.Ordering_API>(
    "ordering-api")
    .WaitFor(orderingMigration)
    .WaitFor(orderingDb)
    .WaitFor(rabbitmq)
    .WithReference(orderingDb)
    .WithReference(rabbitmq)
    .WithHttpsHealthCheck("/health");

// Reverse proxies
var yarpApiGateway = builder.AddProject<Projects.YarpApiGateway>(
    "yarp-api-gateway", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WithReference(basketApi);

// Apps
builder.AddNpmApp("shopping-web", "../WebApps/WebApp.ANG")
    .WithExternalHttpEndpoints()
    .WithReference(yarpApiGateway)
    .WithHttpsEndpoint(env: "PORT")
    .PublishAsDockerFile();

await builder.Build().RunAsync();

static string GetHttpForEndpoints() => "https";
