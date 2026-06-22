#pragma warning disable CA2252 // Opt in to preview features
using AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddForwardedHeaders();

// Cache
var redis = builder.AddRedis("redis");

// Dados: DynamoDB Local (oficial AWS via Aspire.Hosting.AWS) — um por serviço.
// Cada serviço recebe AWS_ENDPOINT_URL_DYNAMODB via WithReference e o SDK resolve sozinho.
var catalogDb = builder.AddAWSDynamoDBLocal("catalogDb");
var basketDb = builder.AddAWSDynamoDBLocal("basketDb");
var discountDb = builder.AddAWSDynamoDBLocal("discountDb");
var orderingDb = builder.AddAWSDynamoDBLocal("orderingDb");

const string orderingTableName = "OrderingTable";

// Cria as tabelas do Ordering (single-table + ProcessedIntegrationEvents) e faz o seed.
var orderingMigration = builder.AddProject<Projects.Ordering_MigrationService>("ordering-migration")
    .WaitFor(orderingDb)
    .WithReference(orderingDb)
    .WithAwsDevEnvironment();

// Lambdas (AWS-first): orquestradas localmente pelo emulador do Aspire.Hosting.AWS.
// EventBridge NÃO é criado localmente (bus só na AWS) — o publish é best-effort.
builder.AddAWSLambdaServiceEmulator();

// EventBridge → SQS → esta Lambda: o gatilho SQS é AWS-only (sem SQS local).
// A função fica registrada para deploy/teste de integração na AWS.
builder.AddAWSLambdaFunction<Projects.Ordering_BasketCheckoutConsumer_Lambda>(
        "ordering-basket-checkout-consumer",
        lambdaHandler: "Ordering.BasketCheckoutConsumer.Lambda::Ordering.BasketCheckoutConsumer.Lambda.Function::FunctionHandler")
    .WaitForCompletion(orderingMigration)
    .WithReference(orderingDb)
    .WithAwsDevEnvironment()
    .WithEnvironment("EventBridge__BusName", "duckstore-event-bus");

// DynamoDB Streams → esta Lambda: roda LOCAL (DynamoDB Local suporta Streams).
builder.AddAWSLambdaFunction<Projects.Ordering_OrderCreatedPublisher_Lambda>(
        "ordering-order-created-publisher",
        lambdaHandler: "Ordering.OrderCreatedPublisher.Lambda::Ordering.OrderCreatedPublisher.Lambda.Function::FunctionHandler")
    .WaitForCompletion(orderingMigration)
    .WithReference(orderingDb)
    .WithDynamoDBStreamsEventSource(orderingTableName)
    .WithAwsDevEnvironment()
    .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
    .WithEnvironment("FeatureManagement__OrderFullfilment", "true");

// Observability
var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
    .WithEnvironment("xpack.security.enabled", "false")
    .WithEnvironment("discovery.type", "single-node")
    .WithDataVolume();

_ = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana", "8.17.3")
    .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
    .WithHttpEndpoint(port: 5601, targetPort: 5601, name: "http")
    .WaitFor(elasticsearch)
    .WithReference(elasticsearch);

// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalog-api")
    .WaitFor(catalogDb)
    .WaitFor(elasticsearch)
    .WithReference(catalogDb)
    .WithReference(elasticsearch)
    .WithAwsDevEnvironment()
    .WithHttpHealthCheck("/health");

var discountApi = builder.AddProject<Projects.Discount_Grpc>(
    "discount-api", GetHttpForEndpoints())
    .WaitFor(discountDb)
    .WaitFor(elasticsearch)
    .WithReference(discountDb)
    .WithReference(elasticsearch)
    .WithAwsDevEnvironment();

var basketApi = builder.AddProject<Projects.Basket_API>(
    "basket-api")
    .WaitFor(redis)
    .WaitFor(basketDb)
    .WaitFor(discountApi)
    .WaitFor(elasticsearch)
    .WithReference(redis)
    .WithReference(basketDb)
    .WithReference(discountApi)
    .WithReference(elasticsearch)
    .WithAwsDevEnvironment()
    .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
    .WithHttpHealthCheck("/health");

redis.WithParentRelationship(basketApi);

var orderingApi = builder.AddProject<Projects.Ordering_API>(
    "ordering-api")
    .WaitForCompletion(orderingMigration)
    .WaitFor(orderingDb)
    .WaitFor(elasticsearch)
    .WithReference(orderingDb)
    .WithReference(elasticsearch)
    .WithAwsDevEnvironment()
    .WithEnvironment("EventBridge__BusName", "duckstore-event-bus")
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

builder.AddNpmApp("shopping-web-spa", "../WebApps/Shopping.Web.SPA")
    .WithExternalHttpEndpoints()
    .WaitFor(yarpApiGateway)
    .WithReference(yarpApiGateway)
    .WithEndpoint(port: 4200, targetPort: 4200, scheme: "https", name: "https", env: "PORT", isProxied: false)
    .PublishAsDockerFile();

await builder.Build().RunAsync();
return;

static string GetHttpForEndpoints() => "http";
static string GetHttpsForEndpoints() => "https";
