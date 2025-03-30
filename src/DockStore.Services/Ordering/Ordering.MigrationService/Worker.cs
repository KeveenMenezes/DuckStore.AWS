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
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await db.Database.MigrateAsync(cancellationToken);
        });
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        await SeedCustomerAsync(db);
        await SeedProductAsync(db);
        await SeedOrdersWithItemsAsync(db);
    }

    private static async Task SeedCustomerAsync(ApplicationDbContext db)
    {
        if (!await db.Customers.AnyAsync())
        {
            await db.Customers.AddRangeAsync(InitialData.Customers);
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedProductAsync(ApplicationDbContext db)
    {
        if (!await db.Products.AnyAsync())
        {
            await db.Products.AddRangeAsync(InitialData.Products);
            await db.SaveChangesAsync();
        }
    }

    private static async Task SeedOrdersWithItemsAsync(ApplicationDbContext db)
    {
        if (!await db.Orders.AnyAsync())
        {
            await db.Orders.AddRangeAsync(InitialData.OrdersWithItems);
            await db.SaveChangesAsync();
        }
    }
}