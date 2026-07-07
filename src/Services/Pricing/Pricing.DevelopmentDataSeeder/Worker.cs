using System.Diagnostics;
using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.Prices.Data;
using Pricing.Function.Shared.Data;

namespace Pricing.DevelopmentDataSeeder;

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

            await scope.ServiceProvider.EnsurePricingTablesCreatedAsync();

            var priceRepository = scope.ServiceProvider.GetRequiredService<IPriceRepository>();
            await SeedAsync(priceRepository);

            var gatewayCostRepository = scope.ServiceProvider.GetRequiredService<IGatewayCostRepository>();
            await SeedGatewayCostAsync(gatewayCostRepository);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task SeedAsync(IPriceRepository priceRepository)
    {
        if (await priceRepository.AnyAsync())
            return;

        foreach (var price in PricingInitialData.Prices)
            await priceRepository.PutAsync(price);
    }

    private static async Task SeedGatewayCostAsync(IGatewayCostRepository gatewayCostRepository)
    {
        if (await gatewayCostRepository.AnyAsync())
            return;

        await gatewayCostRepository.PutAsync(GatewayCostInitialData.Simulated);
    }
}
