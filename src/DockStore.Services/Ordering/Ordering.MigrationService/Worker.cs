using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Ordering.Infrastructure.Data;

namespace Ordering.MigrationService;

public class Worker(
    IServiceProvider serviceProvider,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource s_activitySource = new(ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = s_activitySource.StartActivity(
            "Migrating database", ActivityKind.Client);

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        if (!string.Equals(
            environment,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase))
        {
            activity?.AddTag("SkipMigration", "Environment is not Development");
            hostApplicationLifetime.StopApplication();
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await RunMigrationAsync(dbContext, stoppingToken);

            await SeedAsync(dbContext);

        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task RunMigrationAsync(
        ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }

    private static async Task SeedAsync(ApplicationDbContext dbContext)
    {
        await SeedCustomerAsync(dbContext);
        await SeedProductAsync(dbContext);
        await SeedOrdersWithItemsAsync(dbContext);
    }

    private static async Task SeedCustomerAsync(ApplicationDbContext dbContext)
    {
        if (!await dbContext.Customers.AnyAsync())
        {
            await dbContext.Customers.AddRangeAsync(InitialData.Customers);
            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task SeedProductAsync(ApplicationDbContext dbContext)
    {
        if (!await dbContext.Products.AnyAsync())
        {
            await dbContext.Products.AddRangeAsync(InitialData.Products);
            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task SeedOrdersWithItemsAsync(ApplicationDbContext dbContext)
    {
        if (!await dbContext.Orders.AnyAsync())
        {
            await dbContext.Orders.AddRangeAsync(InitialData.OrdersWithItems);
            await dbContext.SaveChangesAsync();
        }
    }
}