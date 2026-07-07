using Mapster;
using Pricing.Function.Modules.Prices.Features.SetNominalPrice;

namespace Pricing.Function;

public record SetNominalPriceRequest(Guid ProductId, decimal NominalPrice, decimal Cost);
public record SetNominalPriceResponse(Guid ProductId, decimal NominalPrice, decimal Cost);

// AppSync Mutation resolver (Lambda-backed per ADR-0009 — validates a monetary value server-side
// before writing it, unlike a plain key lookup). Creates the Price row on first call, updates it
// on subsequent calls — this is how a product gets its nominal price, decoupled from Catalog's
// createProduct (see ADR-0026 §4).
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<SetNominalPriceResponse> SetNominalPrice(
        SetNominalPriceRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<SetNominalPriceCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<SetNominalPriceResponse>();
    }
}
