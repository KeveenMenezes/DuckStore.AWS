using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;

namespace Pricing.DevelopmentDataSeeder;

// Seeds the default active provider (Installments:ActiveProvider, ADR-0028) with a rate table
// that increases with installment count, mirroring how real processors (Stripe/Pagar.me) publish
// their installment fee tables.
public static class GatewayCostInitialData
{
    public static GatewayCost Simulated => GatewayCost.Create(
        GatewayProvider.Of("Simulated"),
        flatFeePerTransaction: 0.39m,
        avistaRatePercent: 2.5m,
        installmentRates: new Dictionary<int, decimal>
        {
            [1] = 3.0m,
            [2] = 4.2m,
            [3] = 5.1m,
            [4] = 6.0m,
            [5] = 6.8m,
            [6] = 7.5m,
            [7] = 8.9m,
            [8] = 10.2m,
            [9] = 11.4m,
            [10] = 12.5m,
            [11] = 13.3m,
            [12] = 14.0m
        });
}
