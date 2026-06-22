using Amazon.DynamoDBv2;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

builder.Services.AddGrpc();

// DynamoDB Local injeta AWS_ENDPOINT_URL_DYNAMODB; o SDK resolve sozinho.
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddScoped<ICouponRepository, DynamoCouponRepository>();

var app = builder.Build();
// Configure the HTTP request pipeline.

await app.Services.EnsureDiscountTableCreatedAsync();

app.MapGrpcService<DiscountService>();

await app.RunAsync();
