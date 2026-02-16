#pragma warning disable CA2252 // Opt in to preview features
using AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// Database
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
var rabbitMq = builder
    .AddRabbitMQ(
        "messageBroker",
        builder.AddParameter("username", secret: true),
        builder.AddParameter("password", secret: true),
        5672)
    .WithManagementPlugin();

// Observability
var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
    .WithEnvironment("xpack.security.enabled", "false")
    .WithEnvironment("discovery.type", "single-node")
    .WithDataVolume();

var kibana = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana", "8.17.3")
    .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
    .WithHttpEndpoint(port: 5601, targetPort: 5601, name: "http")
    .WithReference(elasticsearch)
    .WaitFor(elasticsearch);

// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalog-api")
    .WaitFor(catalogDb)
    .WaitFor(elasticsearch)
    .WithReference(catalogDb)
    .WithReference(elasticsearch)
    .WithHttpHealthCheck("/health");

var discountApi = builder.AddProject<Projects.Discount_Grpc>(
    "discount-api", GetHttpForEndpoints())
    .WaitFor(discountDb)
    .WaitFor(elasticsearch)
    .WithReference(discountDb)
    .WithReference(elasticsearch);

var basketApi = builder.AddProject<Projects.Basket_API>(
    "basket-api")
    .WaitFor(redis)
    .WaitFor(basketDb)
    .WaitFor(discountApi)
    .WaitFor(rabbitMq)
    .WaitFor(elasticsearch)
    .WithReference(redis)
    .WithReference(basketDb)
    .WithReference(discountApi)
    .WithReference(rabbitMq)
    .WithReference(elasticsearch)
    .WithHttpHealthCheck("/health");

redis.WithParentRelationship(basketApi);

var orderingApi = builder.AddProject<Projects.Ordering_API>(
    "ordering-api")
    .WaitFor(orderingMigration)
    .WaitFor(orderingDb)
    .WaitFor(rabbitMq)
    .WaitFor(elasticsearch)
    .WithReference(orderingDb)
    .WithReference(rabbitMq)
    .WithReference(elasticsearch)
    .WithHttpHealthCheck("/health");

// Reverse proxies
var yarpApiGateway = builder.AddProject<Projects.YarpApiGateway>(
    "yarp-api-gateway", GetHttpsForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WithReference(basketApi);

// Apps
builder.AddProject<Projects.Shopping_Web_Server>(
    "shopping-web-server", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(basketApi)
    .WithReference(catalogApi)
    .WithReference(orderingApi);

// AWS Resources (LocalStack for local dev — SQS + DynamoDB)
var localstack = builder.AddContainer("localstack", "localstack/localstack", "latest")
    .WithEnvironment("SERVICES", "sqs,dynamodb")
    .WithEndpoint(port: 4566, targetPort: 4566, name: "gateway");

// Go Notification Service (SQS consumer → DynamoDB)
// The Go service auto-creates the SQS queue and DynamoDB table when AWS_ENDPOINT_URL is set.
builder.AddDockerfile("notification-go", "../Services/Notification/Notification.Go")
    .WithEnvironment("AWS_REGION", "us-east-1")
    .WithEnvironment("AWS_ENDPOINT_URL", localstack.GetEndpoint("gateway"))
    .WithEnvironment("SQS_QUEUE_NAME", "duckstore-notifications")
    .WithEnvironment("DYNAMODB_TABLE", "duckstore-notifications")
    .WaitFor(localstack);

builder.AddNpmApp("shopping-web-spa", "../WebApps/Shopping.Web.SPA")
    .WithExternalHttpEndpoints()
    .WaitFor(yarpApiGateway)
    .WithReference(yarpApiGateway)
    .WithHttpsEndpoint(env: "PORT")
    .PublishAsDockerFile();

await builder.Build().RunAsync();

static string GetHttpForEndpoints() => "http";
static string GetHttpsForEndpoints() => "https";
