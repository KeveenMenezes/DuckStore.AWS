using Pricing.Function.Modules.Campaigns.Domain.Enums;

namespace Pricing.Function.Modules.Campaigns.Domain.ValueObjects;

// Flexible discount dynamics (business rule #3): a campaign discounts either a fixed amount or a
// percentage of the nominal price — never both, never stacked (ADR-0026: one active campaign per
// product at a time).
public class DiscountValue : ValueObject
{
    public DiscountType Type { get; } = default!;
    public decimal Amount { get; }

    protected DiscountValue() { }

    private DiscountValue(DiscountType type, decimal amount)
    {
        Type = type;
        Amount = amount;
    }

    public static DiscountValue Of(DiscountType type, decimal amount)
    {
        if (amount <= 0)
        {
            throw new BadRequestException("Value", amount, "must be greater than zero");
        }

        if (type == DiscountType.Percentage && amount > 100)
        {
            throw new BadRequestException("Value", amount, "a percentage discount cannot exceed 100");
        }

        return new DiscountValue(type, amount);
    }

    public decimal ApplyTo(decimal nominalPrice) => Type switch
    {
        DiscountType.Fixed => Math.Max(0, nominalPrice - Amount),
        DiscountType.Percentage => Math.Max(0, nominalPrice - nominalPrice * Amount / 100m),
        _ => nominalPrice
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Amount;
    }
}
