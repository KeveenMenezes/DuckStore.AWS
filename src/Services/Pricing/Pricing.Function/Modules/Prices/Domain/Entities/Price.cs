namespace Pricing.Function.Modules.Prices.Domain.Entities;

// Pricing owns the nominal price of a product (ADR-0026) — Catalog no longer stores it.
// One item per product, keyed by ProductId; Set/updated independently of product creation.
public class Price : Aggregate<ProductId>
{
    public decimal NominalPrice { get; private set; }
    public decimal Cost { get; private set; }

    public static Price Create(ProductId productId, decimal nominalPrice, decimal cost)
    {
        ValidateNominalPrice(nominalPrice);
        ValidateCost(cost);

        return new Price
        {
            Id = productId,
            NominalPrice = nominalPrice,
            Cost = cost,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(decimal nominalPrice, decimal cost)
    {
        ValidateNominalPrice(nominalPrice);
        ValidateCost(cost);

        NominalPrice = nominalPrice;
        Cost = cost;
        LastModified = DateTime.UtcNow;
    }

    public static Price Load(Guid productId, decimal nominalPrice, decimal cost, DateTime? updatedAt = null) =>
        new()
        {
            Id = ProductId.Of(productId),
            NominalPrice = nominalPrice,
            Cost = cost,
            LastModified = updatedAt
        };

    private static void ValidateNominalPrice(decimal nominalPrice)
    {
        if (nominalPrice <= 0)
        {
            throw new PriceBadRequestException(nominalPrice);
        }
    }

    private static void ValidateCost(decimal cost)
    {
        if (cost <= 0)
        {
            throw new PriceCostBadRequestException(cost);
        }
    }
}
