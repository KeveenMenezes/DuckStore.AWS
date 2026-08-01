using Amazon.DynamoDBv2;
using BuildingBlocks.ServiceDefaults;
using Challenges.DevelopmentDataSeeder;
using Challenges.Function.Modules.Questions.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddScoped<IQuestionRepository, DynamoQuestionRepository>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
