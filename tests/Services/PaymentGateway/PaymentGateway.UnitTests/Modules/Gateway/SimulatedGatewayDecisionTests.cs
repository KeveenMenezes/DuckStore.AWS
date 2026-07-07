namespace PaymentGateway.UnitTests.Modules.Gateway;

public class SimulatedGatewayDecisionTests
{
    [Fact]
    public void Decide_Authorizes_WhenCardAndAmountAreUnremarkable()
    {
        var (authorized, detail) = SimulatedGatewayDecision.Decide("4111111111111111", 100m);

        Assert.True(authorized);
        Assert.StartsWith("SIM-", detail);
    }

    [Fact]
    public void Decide_Declines_WhenCardNumberEndsInFourZeros()
    {
        var (authorized, detail) = SimulatedGatewayDecision.Decide("4111111111110000", 100m);

        Assert.False(authorized);
        Assert.Equal("card_declined", detail);
    }

    [Theory]
    [InlineData(10_001)]
    [InlineData(50_000)]
    public void Decide_Declines_WhenAmountExceedsThreshold(decimal amount)
    {
        var (authorized, detail) = SimulatedGatewayDecision.Decide("4111111111111111", amount);

        Assert.False(authorized);
        Assert.Equal("amount_exceeds_limit", detail);
    }

    [Fact]
    public void Decide_Authorizes_AtExactThresholdBoundary()
    {
        var (authorized, _) = SimulatedGatewayDecision.Decide("4111111111111111", 10_000m);

        Assert.True(authorized);
    }
}
