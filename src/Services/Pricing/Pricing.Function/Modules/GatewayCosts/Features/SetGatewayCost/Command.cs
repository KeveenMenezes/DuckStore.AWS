namespace Pricing.Function.Modules.GatewayCosts.Features.SetGatewayCost;

public record SetGatewayCostCommand(
    string Provider,
    decimal FlatFeePerTransaction,
    decimal AvistaRatePercent,
    Dictionary<int, decimal> InstallmentRates) : ICommand<SetGatewayCostResult>;

public record SetGatewayCostResult(
    string Provider,
    decimal FlatFeePerTransaction,
    decimal AvistaRatePercent,
    Dictionary<int, decimal> InstallmentRates);

public class SetGatewayCostCommandValidator : AbstractValidator<SetGatewayCostCommand>
{
    public SetGatewayCostCommandValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty()
            .WithMessage("Provider is required");

        RuleFor(x => x.FlatFeePerTransaction)
            .GreaterThanOrEqualTo(0)
            .WithMessage("FlatFeePerTransaction must be greater than or equal to zero");

        RuleFor(x => x.AvistaRatePercent)
            .GreaterThanOrEqualTo(0)
            .WithMessage("AvistaRatePercent must be greater than or equal to zero");

        RuleFor(x => x.InstallmentRates)
            .Must(rates => rates.Count > 0)
            .WithMessage("At least one installment rate is required");

        RuleForEach(x => x.InstallmentRates.Keys)
            .GreaterThan(0)
            .WithMessage("Installment counts must be greater than zero");

        RuleForEach(x => x.InstallmentRates.Values)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Installment rates must be greater than or equal to zero");
    }
}
