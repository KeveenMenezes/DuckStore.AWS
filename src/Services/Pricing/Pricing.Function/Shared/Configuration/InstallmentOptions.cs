namespace Pricing.Function.Shared.Configuration;

// Store-level installment policy — which gateway provider's cost table is authoritative
// (Installments:ActiveProvider, only one active at a time per ADR-0028) and the store's own
// margin tolerance (MinMarginPercent — a merchant policy, not a gateway cost, so it stays here
// rather than on GatewayCost). Read directly from the Installments:* configuration keys, set via
// the Installments__* env vars in PricingExtensions.cs.
public class InstallmentOptions
{
    public string ActiveProvider { get; init; } = "Simulated";
    public decimal MinMarginPercent { get; init; } = 5m;

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
                : defaults.MinMarginPercent
        };
    }
}
