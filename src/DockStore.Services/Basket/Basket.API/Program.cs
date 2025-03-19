var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

builder.AddNpgsqlDataSource("basketDb");
builder.AddRedisClient("redis");

//TODO: adicionar hostname na rede interna do Docker, permitindo comunicação via DNS usando o nome do serviço.
// HTTP and GRPC client registrations
builder.Services.AddGrpcClient<DiscountProtoService.DiscountProtoServiceClient>(
    o => o.Address = new(builder.Configuration["Services:DiscountUrl"]!));

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
    .AddValidatorsFromAssembly(assembly)
    .AddMarten(opts =>
    {
        opts.Schema.For<ShoppingCart>().Identity(x => x.UserName);
    })
    .UseLightweightSessions()
    .UseNpgsqlDataSource();

//Injection dependence
builder.Services.AddScoped<IBasketRepository, BasketRepository>();
builder.Services.Decorate<IBasketRepository, CacheBasketRepository>();

var app = builder.Build();
// Configure the HTTP request pipeline.

app.MapDefaultEndpoints();
app.MapCarter();
app.UseExceptionHandler(options => { });

await app.RunAsync();
