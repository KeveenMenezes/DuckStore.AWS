using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

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
            "Provisioning DynamoDB tables", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            await scope.ServiceProvider.EnsureOrderingTablesCreatedAsync();

            var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            await SeedAsync(orderRepository);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task SeedAsync(IOrderRepository orderRepository)
    {
        if (await orderRepository.GetTotalCountOrders() > 0)
            return;

        foreach (var order in InitialData.OrdersWithItems)
            await orderRepository.AddAsync(order);
    }
}