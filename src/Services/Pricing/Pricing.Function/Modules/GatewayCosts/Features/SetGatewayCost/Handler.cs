using Pricing.Function.Modules.GatewayCosts.Data;
using Pricing.Function.Modules.GatewayCosts.Domain.Entities;
using Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;

namespace Pricing.Function.Modules.GatewayCosts.Features.SetGatewayCost;

public class SetGatewayCostHandler(IGatewayCostRepository gatewayCostRepository)
    : ICommandHandler<SetGatewayCostCommand, SetGatewayCostResult>
{
    public async Task<SetGatewayCostResult> Handle(
        SetGatewayCostCommand command, CancellationToken cancellationToken)
    {
        var provider = GatewayProvider.Of(command.Provider);

        var existing = await gatewayCostRepository.GetByProviderAsync(command.Provider, cancellationToken);

        var gatewayCost = existing is null
            ? GatewayCost.Create(provider, command.FlatFeePerTransaction, command.AvistaRatePercent, command.InstallmentRates)
            : existing;

        if (existing is not null)
        {
            gatewayCost.Update(command.FlatFeePerTransaction, command.AvistaRatePercent, command.InstallmentRates);
        }

        await gatewayCostRepository.PutAsync(gatewayCost, cancellationToken);

        return new SetGatewayCostResult(
            gatewayCost.Id.Value,
            gatewayCost.FlatFeePerTransaction,
            gatewayCost.AvistaRatePercent,
            gatewayCost.InstallmentRates.ToDictionary(rate => rate.Key, rate => rate.Value));
    }
}
