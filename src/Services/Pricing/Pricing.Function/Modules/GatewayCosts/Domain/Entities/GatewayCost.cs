namespace Pricing.Function.Modules.GatewayCosts.Domain.Entities;

// Models one payment provider's operating costs (business rule #4 — "engenharia de
// parcelamento"): a flat fee per transaction, a discount rate for upfront (à vista) payment, and
// an explicit rate per installment count — mirroring how real processors (Stripe/Pagar.me)
// publish installment fee tables, rather than a synthetic linear formula (ADR-0028, supersedes
// ADR-0026 §9's "not a table" ruling). Only one provider is "active" at a time
// (Installments:ActiveProvider) — no multi-provider comparison logic.
public class GatewayCost : Aggregate<GatewayProvider>
{
    public decimal FlatFeePerTransaction { get; private set; }
    public decimal AvistaRatePercent { get; private set; }
    public IReadOnlyDictionary<int, decimal> InstallmentRates { get; private set; } =
        new Dictionary<int, decimal>();

    public static GatewayCost Create(
        GatewayProvider provider,
        decimal flatFeePerTransaction,
        decimal avistaRatePercent,
        IReadOnlyDictionary<int, decimal> installmentRates)
    {
        Validate(flatFeePerTransaction, avistaRatePercent, installmentRates);

        return new GatewayCost
        {
            Id = provider,
            FlatFeePerTransaction = flatFeePerTransaction,
            AvistaRatePercent = avistaRatePercent,
            InstallmentRates = installmentRates,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(
        decimal flatFeePerTransaction,
        decimal avistaRatePercent,
        IReadOnlyDictionary<int, decimal> installmentRates)
    {
        Validate(flatFeePerTransaction, avistaRatePercent, installmentRates);

        FlatFeePerTransaction = flatFeePerTransaction;
        AvistaRatePercent = avistaRatePercent;
        InstallmentRates = installmentRates;
        LastModified = DateTime.UtcNow;
    }

    public static GatewayCost Load(
        string provider,
        decimal flatFeePerTransaction,
        decimal avistaRatePercent,
        IReadOnlyDictionary<int, decimal> installmentRates,
        DateTime? updatedAt = null) =>
        new()
        {
            Id = GatewayProvider.Of(provider),
            FlatFeePerTransaction = flatFeePerTransaction,
            AvistaRatePercent = avistaRatePercent,
            InstallmentRates = installmentRates,
            LastModified = updatedAt
        };

    private static void Validate(
        decimal flatFeePerTransaction,
        decimal avistaRatePercent,
        IReadOnlyDictionary<int, decimal> installmentRates)
    {
        if (flatFeePerTransaction < 0)
        {
            throw new GatewayCostBadRequestException("FlatFeePerTransaction must be greater than or equal to zero");
        }

        if (avistaRatePercent < 0)
        {
            throw new GatewayCostBadRequestException("AvistaRatePercent must be greater than or equal to zero");
        }

        if (installmentRates.Count == 0)
        {
            throw new GatewayCostBadRequestException("At least one installment rate is required");
        }

        if (installmentRates.Keys.Any(installments => installments <= 0))
        {
            throw new GatewayCostBadRequestException("Installment counts must be greater than zero");
        }

        if (installmentRates.Values.Any(rate => rate < 0))
        {
            throw new GatewayCostBadRequestException("Installment rates must be greater than or equal to zero");
        }

        if (!installmentRates.ContainsKey(1))
        {
            throw new GatewayCostBadRequestException(
                "InstallmentRates must include a rate for installment count 1 — the pricing engine backs " +
                "the 1x card price out of this rate");
        }
    }
}
