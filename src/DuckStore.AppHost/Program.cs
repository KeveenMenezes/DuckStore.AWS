var builder = DistributedApplication.CreateBuilder(args);

//Create DataBase 
var catalogDb = builder.AddPostgres("catalogDb");

// Services
var catalogApi = builder.AddProject<Projects.Catalog_API>(
    "catalog-api", GetHttpForEndpoints())
    .WithExternalHttpEndpoints()
    .WithReference(catalogDb);

await builder.Build().RunAsync();

static string GetHttpForEndpoints() =>
    // Attempt to parse the environment variable value; return true if it's exactly "1".
    int.TryParse(
        Environment.GetEnvironmentVariable("ESHOP_USE_HTTP_ENDPOINTS"),
        out int result) && result == 1 ? "http" : "https";
