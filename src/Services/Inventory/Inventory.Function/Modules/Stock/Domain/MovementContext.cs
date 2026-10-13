namespace Inventory.Function.Modules.Stock.Domain;

// Who and when, plus the idempotency key the client minted for this attempt and the fingerprint of the
// request it came with (ADR-0052 §7). Actor is the Cognito sub from the token, never client input.
public sealed record MovementContext(Guid IdempotencyKey, string Actor, DateTime At, string PayloadHash);
