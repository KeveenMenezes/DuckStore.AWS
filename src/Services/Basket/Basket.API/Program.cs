using BuildingBlocks.Core.Abstractions;
using BuildingBlocks.ServiceDefaults.Behaviors;
using BuildingBlocks.ServiceDefaults.ExceptionHandler;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();
builder.AddRabbitMQClient("messageBroker");
builder.AddNpgsqlDataSource("basketDb");
builder.AddRedisClient("redis");

// HTTP and GRPC client registrations
builder.Services.AddGrpcClient<DiscountProtoService.DiscountProtoServiceClient>(
    o => o.Address = new("http://discount-api"));

var assembly = typeof(Program).Assembly;
builder.Services
    .AddExceptionHandler<CustomExceptionHandler>()
    .AddCarter()
    .AddMediatR(config =>
    {
        config.RegisterServicesFromAssembly(assembly);
        config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        config.AddOpenBehavior(typeof(LoggingBehavior<,>));
        config.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
    })
    .AddValidatorsFromAssembly(assembly)
    .AddMarten(opts =>
    {
        opts.Schema.For<ShoppingCart>().Identity(x => x.UserName);
    })
    .UseLightweightSessions()
    .UseNpgsqlDataSource();

//Injection dependence
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IBasketRepository, BasketRepository>();
builder.Services.Decorate<IBasketRepository, CacheBasketRepository>();

//Async Communication Services
builder.Services.AddMessageBroker(
    builder.Configuration, Assembly.GetExecutingAssembly());

var app = builder.Build();
// Configure the HTTP request pipeline.

app.MapDefaultEndpoints();
app.MapCarter();
app.UseExceptionHandler(options => { });

await app.RunAsync();
