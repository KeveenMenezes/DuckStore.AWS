using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CatalogView.Function.Modules.Products.Data;
using CatalogView.Function.Modules.Products.Domain;
using Microsoft.Extensions.Configuration;

namespace CatalogView.DevelopmentDataSeeder;

// One-time historical backfill (ADR-0027): scans Catalog's `products` table for product fields,
// Review's `reviews` table (grouped by ProductId) for historical rating totals, Pricing's `prices`
// table for nominal prices and cost (ADR-0026 — Catalog products no longer carry a price),
// Pricing's `gateway-costs` table for the active provider's payment badge (ADR-0028 — a manual
// re-run is the only way to refresh badges after a gateway-cost-only change, since that alone
// never fans out via CDC), and Pricing's `product-discounts` table for any active campaign
// discount, then bulk-indexes complete SearchDocuments into OpenSearch before any steady-state CDC
// traffic flows. This is an explicit, scoped exception to "no cross-context table reads" — a
// migration tool, not a runtime coupling. Never runs again after the initial rollout (§8 of the plan).
public sealed class ProductBackfill(IAmazonDynamoDB dynamoDb, IProductSearchIndex index, IConfiguration configuration)
{
    private const string ProductsTableName = "products";
    private const string ReviewsTableName = "reviews";
    private const string PricesTableName = "prices";
    private const string GatewayCostsTableName = "gateway-costs";
    private const string ProductDiscountsTableName = "product-discounts";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await index.EnsureIndexAsync(cancellationToken);

        var ratingTotals = await ScanRatingTotalsAsync(cancellationToken);
        var prices = await ScanPricesAsync(cancellationToken);
        var gatewayCost = await GetActiveGatewayCostAsync(cancellationToken);
        var discounts = await ScanActiveDiscountsAsync(cancellationToken);
        var documents = await ScanProductsAsync(ratingTotals, prices, gatewayCost, discounts, cancellationToken);

        await index.BulkIndexAsync(documents, cancellationToken);
    }

    private async Task<Dictionary<string, (decimal NominalPrice, decimal Cost)>> ScanPricesAsync(
        CancellationToken cancellationToken)
    {
        var prices = new Dictionary<string, (decimal NominalPrice, decimal Cost)>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await dynamoDb.ScanAsync(
                new ScanRequest
                {
                    TableName = PricesTableName,
                    ExclusiveStartKey = lastKey,
                    ProjectionExpression = "ProductId, NominalPrice, Cost"
                },
                cancellationToken);

            foreach (var item in response.Items ?? [])
            {
                if (item.TryGetValue("ProductId", out var productIdAttr) &&
                    item.TryGetValue("NominalPrice", out var priceAttr) &&
                    !string.IsNullOrEmpty(priceAttr.N))
                {
                    var cost = item.TryGetValue("Cost", out var costAttr) && !string.IsNullOrEmpty(costAttr.N)
                        ? decimal.Parse(costAttr.N, CultureInfo.InvariantCulture)
                        : 0m;

                    prices[productIdAttr.S] = (decimal.Parse(priceAttr.N, CultureInfo.InvariantCulture), cost);
                }
            }

            lastKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        } while (lastKey is not null);

        return prices;
    }

    // GetItem, not a scan — only the single active provider row matters (Installments:ActiveProvider,
    // ADR-0028). Decoding is duplicated from Pricing.Function's DynamoGatewayCostRepository rather
    // than adding a cross-project reference for a one-time migration tool.
    private async Task<BackfillGatewayCost?> GetActiveGatewayCostAsync(CancellationToken cancellationToken)
    {
        var activeProvider = configuration["Installments:ActiveProvider"] ?? "Simulated";

        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = GatewayCostsTableName,
                Key = new Dictionary<string, AttributeValue> { ["Provider"] = new(activeProvider) }
            },
            cancellationToken);

        if (response.Item is not { Count: > 0 })
            return null;

        var flatFee = decimal.Parse(response.Item["FlatFeePerTransaction"].N, CultureInfo.InvariantCulture);
        var installmentRates = response.Item["InstallmentRates"].M.ToDictionary(
            rate => int.Parse(rate.Key, CultureInfo.InvariantCulture),
            rate => decimal.Parse(rate.Value.N, CultureInfo.InvariantCulture));

        var minMarginPercent = decimal.TryParse(
            configuration["Installments:MinMarginPercent"], NumberStyles.Number, CultureInfo.InvariantCulture, out var margin)
            ? margin
            : 5m;

        return new BackfillGatewayCost(flatFee, installmentRates, minMarginPercent, ParseValueTiers(configuration));
    }

    // Mirrors InstallmentOptions.ParseValueTiers (Pricing.Function) — same defensive, gap-stops
    // parsing, same ascending sort so BackfillGatewayCost can do a simple forward scan.
    private static List<(decimal MinAmount, int MaxInstallments)> ParseValueTiers(IConfiguration configuration)
    {
        var tiers = new List<(decimal MinAmount, int MaxInstallments)>();

        for (var index = 0; ; index++)
        {
            var minAmountParsed = decimal.TryParse(
                configuration[$"Installments:ValueTiers:{index}:MinAmount"],
                NumberStyles.Number, CultureInfo.InvariantCulture, out var minAmount);
            var maxInstallmentsParsed = int.TryParse(
                configuration[$"Installments:ValueTiers:{index}:MaxInstallments"],
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxInstallments);

            if (!minAmountParsed || !maxInstallmentsParsed)
                break;

            tiers.Add((minAmount, maxInstallments));
        }

        return [.. tiers.OrderBy(t => t.MinAmount)];
    }

    private async Task<Dictionary<string, (int Sum, int Count)>> ScanRatingTotalsAsync(
        CancellationToken cancellationToken)
    {
        var totals = new Dictionary<string, (int Sum, int Count)>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await dynamoDb.ScanAsync(
                new ScanRequest
                {
                    TableName = ReviewsTableName,
                    ExclusiveStartKey = lastKey,
                    ProjectionExpression = "ProductId, Rating"
                },
                cancellationToken);

            foreach (var item in response.Items ?? [])
            {
                if (!item.TryGetValue("ProductId", out var productIdAttr) ||
                    !item.TryGetValue("Rating", out var ratingAttr))
                    continue;

                var rating = int.Parse(ratingAttr.N, CultureInfo.InvariantCulture);
                var (sum, count) = totals.TryGetValue(productIdAttr.S, out var existing)
                    ? existing
                    : (0, 0);

                totals[productIdAttr.S] = (sum + rating, count + 1);
            }

            lastKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        } while (lastKey is not null);

        return totals;
    }

    // Same expiry check as the currentDiscountForProduct AppSync resolver / Pricing's
    // GetActiveDiscountForProductAsync — a discount past EndsAt (or not yet StartsAt) is skipped.
    private async Task<Dictionary<string, (string DiscountType, decimal Value)>> ScanActiveDiscountsAsync(
        CancellationToken cancellationToken)
    {
        var discounts = new Dictionary<string, (string DiscountType, decimal Value)>();
        var now = DateTime.UtcNow;
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await dynamoDb.ScanAsync(
                new ScanRequest
                {
                    TableName = ProductDiscountsTableName,
                    ExclusiveStartKey = lastKey,
                    ProjectionExpression = "ProductId, DiscountType, #v, StartsAt, EndsAt",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#v"] = "Value" }
                },
                cancellationToken);

            foreach (var item in response.Items ?? [])
            {
                if (!item.TryGetValue("ProductId", out var productIdAttr) ||
                    !item.TryGetValue("DiscountType", out var typeAttr) ||
                    !item.TryGetValue("Value", out var valueAttr) ||
                    !item.TryGetValue("StartsAt", out var startsAtAttr) ||
                    !item.TryGetValue("EndsAt", out var endsAtAttr))
                    continue;

                var startsAt = DateTime.Parse(startsAtAttr.S, null, DateTimeStyles.RoundtripKind);
                var endsAt = DateTime.Parse(endsAtAttr.S, null, DateTimeStyles.RoundtripKind);

                if (now < startsAt || now > endsAt)
                    continue;

                discounts[productIdAttr.S] = (typeAttr.S, decimal.Parse(valueAttr.N, CultureInfo.InvariantCulture));
            }

            lastKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        } while (lastKey is not null);

        return discounts;
    }

    private async Task<List<SearchDocument>> ScanProductsAsync(
        Dictionary<string, (int Sum, int Count)> ratingTotals,
        Dictionary<string, (decimal NominalPrice, decimal Cost)> prices,
        BackfillGatewayCost? gatewayCost,
        Dictionary<string, (string DiscountType, decimal Value)> discounts,
        CancellationToken cancellationToken)
    {
        var documents = new List<SearchDocument>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await dynamoDb.ScanAsync(
                new ScanRequest { TableName = ProductsTableName, ExclusiveStartKey = lastKey },
                cancellationToken);

            foreach (var item in response.Items ?? [])
                documents.Add(ToDocument(item, ratingTotals, prices, gatewayCost, discounts));

            lastKey = response.LastEvaluatedKey is { Count: > 0 } ? response.LastEvaluatedKey : null;
        } while (lastKey is not null);

        return documents;
    }

    private static SearchDocument ToDocument(
        Dictionary<string, AttributeValue> item,
        Dictionary<string, (int Sum, int Count)> ratingTotals,
        Dictionary<string, (decimal NominalPrice, decimal Cost)> prices,
        BackfillGatewayCost? gatewayCost,
        Dictionary<string, (string DiscountType, decimal Value)> discounts)
    {
        var id = item["Id"].S;
        var (sum, count) = ratingTotals.TryGetValue(id, out var totals) ? totals : (0, 0);
        var (originalPrice, cost) = prices.TryGetValue(id, out var priceEntry) ? priceEntry : (0m, 0m);

        var breakdown = gatewayCost is null || originalPrice <= 0 || cost <= 0
            ? (Price: 0m, CashPrice: 0m, MaxInstallments: 0, MaxInstallmentValue: 0m, Plan: new List<(int, decimal, decimal, bool)>())
            : gatewayCost.Calculate(cost, originalPrice);

        if (gatewayCost is not null && discounts.TryGetValue(id, out var discount))
        {
            breakdown = gatewayCost.ApplyDiscount(
                breakdown.Price, breakdown.CashPrice, originalPrice, discount.DiscountType, discount.Value);
        }

        return new SearchDocument
        {
            Id = id,
            Name = item["Name"].S,
            Description = item["Description"].S,
            ImageUrl = item["ImageUrl"].S,
            OriginalPrice = originalPrice,
            Stock = int.Parse(item["Stock"].N, CultureInfo.InvariantCulture),
            CategoryIds = item.TryGetValue("CategoryIds", out var categoryIds) && categoryIds.SS is not null
                ? [.. categoryIds.SS]
                : [],
            RatingSum = sum,
            RatingCount = count,
            AverageRating = count == 0 ? 0 : (double)sum / count,
            Price = breakdown.Price,
            CashPrice = breakdown.CashPrice,
            MaxInstallmentsWithoutInterest = breakdown.MaxInstallments,
            MaxInstallmentValue = breakdown.MaxInstallmentValue
        };
    }

    // Local mirror of Pricing.Function's GatewayCost + InstallmentCalculator (ADR-0028/0030) — kept
    // deliberately duplicated rather than cross-referencing Pricing.Function from this seeder.
    private sealed record BackfillGatewayCost(
        decimal FlatFeePerTransaction,
        Dictionary<int, decimal> InstallmentRates,
        decimal MinMarginPercent,
        List<(decimal MinAmount, int MaxInstallments)> ValueTiers)
    {
        public (decimal Price, decimal CashPrice, int MaxInstallments, decimal MaxInstallmentValue, List<(int, decimal, decimal, bool)> Plan)
            Calculate(decimal cost, decimal originalPrice)
        {
            var floor = cost * (1 + MinMarginPercent / 100m);
            var cashPrice = Round(floor + FlatFeePerTransaction);

            var rate1 = InstallmentRates[1];
            var price = Round((floor + FlatFeePerTransaction) / (1 - rate1 / 100m));

            var (maxInstallments, maxInstallmentValue, plan) = BuildPlan(price, originalPrice);

            return (price, cashPrice, maxInstallments, maxInstallmentValue, plan);
        }

        public (decimal Price, decimal CashPrice, int MaxInstallments, decimal MaxInstallmentValue, List<(int, decimal, decimal, bool)> Plan)
            ApplyDiscount(
                decimal price, decimal cashPrice, decimal originalPrice, string discountType, decimal discountValue)
        {
            var discountedPrice = discountType == "Fixed"
                ? Math.Max(0, price - discountValue)
                : Math.Max(0, price - price * discountValue / 100m);

            discountedPrice = Round(discountedPrice);

            var (maxInstallments, maxInstallmentValue, plan) = BuildPlan(discountedPrice, originalPrice);

            return (discountedPrice, cashPrice, maxInstallments, maxInstallmentValue, plan);
        }

        // Hybrid cap (ADR-0028 §2): final limit is the higher of the margin-based ceiling walk and
        // the value-tier lookup on originalPrice (this product's own price, i.e. a "cart of 1"),
        // capped to the highest installment count the active provider's rate table actually offers.
        private (int MaxInstallments, decimal MaxInstallmentValue, List<(int, decimal, decimal, bool)> Plan) BuildPlan(
            decimal price, decimal originalPrice)
        {
            var marginBasedLimit = ComputeMarginBasedLimit(price, originalPrice);
            var tierBasedLimit = ComputeTierBasedLimit(originalPrice);
            var highestAvailableCount = InstallmentRates.Keys.Count == 0 ? 1 : InstallmentRates.Keys.Max();
            var finalLimit = Math.Min(Math.Max(marginBasedLimit, tierBasedLimit), highestAvailableCount);

            var maxInstallmentValue = price;
            var plan = new List<(int, decimal, decimal, bool)>();

            foreach (var count in InstallmentRates.Keys.Where(k => k >= 2).OrderBy(k => k))
            {
                var rate = InstallmentRates[count];
                var totalAtCount = price * (1 + rate / 100m);
                var value = Round(totalAtCount / count);
                var hasInterest = count > finalLimit;

                if (!hasInterest)
                    maxInstallmentValue = value;

                plan.Add((count, value, value * count, hasInterest));
            }

            return (finalLimit, maxInstallmentValue, plan);
        }

        private int ComputeMarginBasedLimit(decimal price, decimal originalPrice)
        {
            var limit = 1;
            var bufferExhausted = false;

            foreach (var count in InstallmentRates.Keys.Where(k => k >= 2).OrderBy(k => k))
            {
                var totalAtCount = price * (1 + InstallmentRates[count] / 100m);

                if (!bufferExhausted && totalAtCount <= originalPrice)
                    limit = count;
                else
                    bufferExhausted = true;
            }

            return limit;
        }

        private int ComputeTierBasedLimit(decimal totalBasketPrice)
        {
            var limit = 0;

            foreach (var tier in ValueTiers)
            {
                if (tier.MinAmount > totalBasketPrice)
                    break;

                limit = tier.MaxInstallments;
            }

            return limit;
        }

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
