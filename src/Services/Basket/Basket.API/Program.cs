using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults.Behaviors;
using BuildingBlocks.ServiceDefaults.ExceptionHandler;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();
builder.AddElasticsearch();
builder.AddRedisClient("redis");

// HTTP and GRPC client registrations
builder.Services.AddGrpcClient<DiscountProtoService.DiscountProtoServiceClient>(
    o => o.Address = new Uri("http://discount-api"));

var assembly = typeof(Program).Assembly;
builder.Services
    .AddExceptionHandler<CustomExceptionHandler>()
    .AddCarter()
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(assembly);
        config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(LoggingBehavior<,>));
    })
    .AddValidatorsFromAssembly(assembly);

//Injection dependence — DynamoDB Local injeta AWS_ENDPOINT_URL_DYNAMODB; o SDK resolve sozinho.
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddScoped<IBasketRepository, BasketRepository>();
builder.Services.Decorate<IBasketRepository, CacheBasketRepository>();

//Async Communication Services
builder.Services.AddEventBridgeMessaging(builder.Configuration);

var app = builder.Build();
// Configure the HTTP request pipeline.

await app.Services.EnsureBasketTableCreatedAsync();

app.MapDefaultEndpoints();
app.MapCarter();
app.UseExceptionHandler(options => { });

await app.RunAsync();
