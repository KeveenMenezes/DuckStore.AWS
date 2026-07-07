namespace Pricing.Function.Modules.GatewayCosts.Domain.ValueObjects;

public class GatewayProvider : ValueObject<string>
{
    private GatewayProvider(string value) : base(value) { }

    public static GatewayProvider Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GatewayCostBadRequestException("Provider name must not be empty");
        }

        return new GatewayProvider(value);
    }
}
