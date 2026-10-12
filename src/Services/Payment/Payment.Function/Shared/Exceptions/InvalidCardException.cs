namespace Payment.Function.Shared.Exceptions;

// A card the checkout refuses (ADR-0050 §6). Field names the offending input (CardNumber,
// Expiration or Cvv) so the SPA can mark it. The message reaches the client, so it never carries
// the card value.
public class InvalidCardException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
