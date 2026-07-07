namespace Pricing.Function.Modules.Prices.Features.SetNominalPrice;

public class SetNominalPriceHandler(IPriceRepository priceRepository)
    : ICommandHandler<SetNominalPriceCommand, SetNominalPriceResult>
{
    public async Task<SetNominalPriceResult> Handle(
        SetNominalPriceCommand command, CancellationToken cancellationToken)
    {
        var productId = ProductId.Of(command.ProductId);

        var existing = await priceRepository.GetByProductIdAsync(command.ProductId, cancellationToken);

        var price = existing is null
            ? Price.Create(productId, command.NominalPrice, command.Cost)
            : existing;

        if (existing is not null)
        {
            price.Update(command.NominalPrice, command.Cost);
        }

        await priceRepository.PutAsync(price, cancellationToken);

        return new SetNominalPriceResult(price.Id.Value, price.NominalPrice, price.Cost);
    }
}
