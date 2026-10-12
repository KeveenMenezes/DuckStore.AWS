namespace Basket.Function.Modules.ShoppingCarts.Data;

// How a checkout's atomic cart close ended (ADR-0050 §4). Replayed carries the OrderId the first
// close of the same attempt stored; CartChanged means the cart is no longer the version quoted.
public enum CartCloseOutcome
{
    Closed,
    Replayed,
    CartChanged
}

public sealed record CartCloseResult(CartCloseOutcome Outcome, Guid OrderId);
