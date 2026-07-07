using Pricing.Function.Modules.Prices.Domain.Entities;
using Pricing.Function.Modules.Prices.Domain.ValueObjects;

namespace Pricing.DevelopmentDataSeeder;

// Mirrors the nominal prices Catalog used to embed directly on Product before ADR-0026 moved
// price ownership to Pricing — proves the two-step "createProduct then setNominalPrice" flow
// works end-to-end in local dev, for the same 8 products Catalog seeds.
//
// Costs are deliberately NOT a uniform ~50% of originalPrice: every product below carries a
// tight-enough margin (cost close to originalPrice) that InstallmentCalculator's cost-floor price
// lands close enough to the originalPrice ceiling for the gateway's rate table
// (GatewayCostInitialData) to exhaust the interest-free buffer well before 12x — caps land at
// 2x/3x/4x, varying per product — instead of every product coasting to the table's max. This
// exercises the bufferExhausted one-way latch and the real-interest UI path, while also keeping
// each product's maxInstallmentValue (the $ amount at the last interest-free count) at $10+, since
// a lower cap divides the near-ceiling total across fewer installments.
public static class PricingInitialData
{
    public static IEnumerable<Price> Prices =>
    [
        // price=28.49 -> caps interest-free at 2x of $14.84.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000001")), 29.90m, 25.95m),
        // price=37.69 -> caps interest-free at 3x of $13.20.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000002")), 39.90m, 34.45m),
        // price=37.82 -> caps interest-free at 3x of $13.25.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000003")), 39.90m, 34.57m),
        // price=56.30 -> caps interest-free at 4x of $14.92.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000004")), 59.90m, 51.64m),
        // price=46.79 -> caps interest-free at 4x of $12.40.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000005")), 49.90m, 42.85m),
        // price=42.11 -> caps interest-free at 4x of $11.16.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000006")), 44.90m, 38.53m),
        // price=42.11 -> caps interest-free at 4x of $11.16.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000007")), 44.90m, 38.53m),
        // price=37.69 -> caps interest-free at 3x of $13.20.
        Price.Create(ProductId.Of(new Guid("b1000000-0000-0000-0000-000000000008")), 39.90m, 34.45m),
    ];
}
