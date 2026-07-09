namespace Pricing.Function.Shared.Configuration;

// Store-level installment policy — which gateway provider's cost table is authoritative
// (Installments:ActiveProvider, only one active at a time per ADR-0028) and the store's own
// margin tolerance (MinMarginPercent — a merchant policy, not a gateway cost, so it stays here
// rather than on GatewayCost). ValueTiers is the hybrid model's second, independent unlock path
// (ADR-0028 §2): a cart-total floor on installments that can exceed what margin alone would allow,
// never reduce it. Read directly from the Installments:* configuration keys, set via the
// Installments__* env vars in PricingExtensions.cs.
public class InstallmentOptions
{
    public string ActiveProvider { get; init; } = "Simulated";
    public decimal MinMarginPercent { get; init; } = 5m;
    public IReadOnlyList<ValueTier> ValueTiers { get; init; } = [];

    public static InstallmentOptions FromConfiguration(IConfiguration configuration)
    {
        var defaults = new InstallmentOptions();

        return new InstallmentOptions
        {
            ActiveProvider = configuration["Installments:ActiveProvider"] ?? defaults.ActiveProvider,
            MinMarginPercent = decimal.TryParse(
                configuration["Installments:MinMarginPercent"],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var margin)
                ? margin
                : defaults.MinMarginPercent,
            ValueTiers = ParseValueTiers(configuration)
        };
    }

    // Same defensive, manual-parse style as the fields above (not configuration.Get<List<T>>(),
    // which has no fallback-to-default on a malformed entry). Stops at the first index that fails
    // to parse — a half-configured tier list is treated as a gap, not skipped over — then sorts
    // ascending by MinAmount so InstallmentCalculator can do a simple forward scan.
    private static List<ValueTier> ParseValueTiers(IConfiguration configuration)
    {
        var tiers = new List<ValueTier>();

        for (var index = 0; ; index++)
        {
            var minAmountParsed = decimal.TryParse(
                configuration[$"Installments:ValueTiers:{index}:MinAmount"],
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var minAmount);
            var maxInstallmentsParsed = int.TryParse(
                configuration[$"Installments:ValueTiers:{index}:MaxInstallments"],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxInstallments);

            if (!minAmountParsed || !maxInstallmentsParsed)
                break;

            tiers.Add(new ValueTier(minAmount, maxInstallments));
        }

        return [.. tiers.OrderBy(t => t.MinAmount)];
    }
}
