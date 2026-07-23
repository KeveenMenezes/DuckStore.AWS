namespace PaymentGateway.Function.Modules.Gateway;

// Deterministic simulated authorize/decline decision — a stand-in for a real payment provider
// (ADR-0025). No real gateway is called: this exists purely to illustrate the authorize/decline
// branch, not to model real fraud logic. Declines a classic "always-declines" test card suffix
// or an arbitrary simulated fraud threshold; otherwise authorizes with a fabricated code.
public static class SimulatedGatewayDecision
{
    private const decimal FraudThreshold = 10_000m;

    public static (bool Authorized, string Detail) Decide(string cardNumber, decimal amount)
    {
        if (cardNumber.EndsWith("0000", StringComparison.Ordinal))
            return (false, "card_declined");

        if (amount > FraudThreshold)
            return (false, "amount_exceeds_limit");

        return (true, $"SIM-{Guid.NewGuid()}");
    }
}
