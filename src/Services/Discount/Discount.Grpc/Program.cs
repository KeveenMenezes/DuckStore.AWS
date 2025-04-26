var builder = WebApplication.CreateBuilder(args);
// Add services to the container.

builder.AddServiceDefaults();

builder.Services.AddGrpc();
builder.AddSqliteDbContext<DiscountContext>("discountDb");

var app = builder.Build();
// Configure the HTTP request pipeline.

app.UseMigration();
app.MapGrpcService<DiscountService>();

await app.RunAsync();
