using Pricing.Function.Modules.GatewayCosts.Domain.Entities;

namespace Pricing.Function.Modules.GatewayCosts.Data;

public interface IGatewayCostRepository
{
    Task<GatewayCost?> GetByProviderAsync(string provider, CancellationToken cancellationToken = default);
    Task PutAsync(GatewayCost gatewayCost, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}
