using Pricing.Function.Modules.GatewayCosts.Features.SetGatewayCost;

namespace Pricing.Function;

// AppSync request/response — InstallmentRates travels as a plain string-keyed map (the GraphQL
// AWSJSON scalar arrives as a JSON object here, not a double-encoded string) since AppSync has no
// integer-keyed map type; keys are parsed to int before building the command.
public record SetGatewayCostRequest(
    string Provider, decimal FlatFeePerTransaction, decimal AvistaRatePercent, Dictionary<string, decimal> InstallmentRates);
public record SetGatewayCostResponse(
    string Provider, decimal FlatFeePerTransaction, decimal AvistaRatePercent, Dictionary<string, decimal> InstallmentRates);

// AppSync Mutation resolver (Lambda-backed per ADR-0009 — validates monetary values and the
// installment-rate table server-side before writing them).
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<SetGatewayCostResponse> SetGatewayCost(
        SetGatewayCostRequest request,
        [FromServices] ISender sender)
    {
        var command = new SetGatewayCostCommand(
            request.Provider,
            request.FlatFeePerTransaction,
            request.AvistaRatePercent,
            request.InstallmentRates.ToDictionary(
                rate => int.Parse(rate.Key, CultureInfo.InvariantCulture), rate => rate.Value));

        var result = await sender.Send(command, CancellationToken.None);

        return new SetGatewayCostResponse(
            result.Provider,
            result.FlatFeePerTransaction,
            result.AvistaRatePercent,
            result.InstallmentRates.ToDictionary(
                rate => rate.Key.ToString(CultureInfo.InvariantCulture), rate => rate.Value));
    }
}
