namespace Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("orderingDb");

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors();
            options.UseSqlServer(connectionString);
        });

        return services;
    }
}
