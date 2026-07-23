namespace Basket.Function.Modules.ShoppingCarts.Domain.Dtos;

// Mirrors BasketCheckoutEvent's shape exactly: CheckoutBasketCommandHandler serializes this to
// JSON (stored in the cart row's CheckoutData attribute), and CheckoutedRule deserializes that
// same JSON directly as BasketCheckoutEvent — the two types' property structure must stay in sync.
public class BasketCheckoutDto
{
    public string OwnerId { get; init; } = null!;
    public Guid CustomerId { get; set; }
    public decimal TotalPrice { get; init; }

    // Generated server-side by CheckoutBasketCommandHandler, not supplied by the client (ADR-0038).
    public Guid OrderId { get; set; }

    public BasketCheckoutAddressDto ShippingAddress { get; set; } = new();
    public BasketCheckoutPaymentDto Payment { get; set; } = new();

    // Populated server-side from the cart being checked out, not supplied by the client —
    // Ordering.Function reads this to build the real OrderItems (previously hardcoded).
    public List<BasketCheckoutItemDto> Items { get; set; } = [];
}

public class BasketCheckoutAddressDto
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string EmailAddress { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string State { get; set; } = null!;
    public string ZipCode { get; set; } = null!;
}

public class BasketCheckoutPaymentDto
{
    public string CardName { get; set; } = null!;
    public string CardNumber { get; set; } = null!;
    public string Expiration { get; set; } = null!;
    public string Cvv { get; set; } = null!;
    public int PaymentMethod { get; set; }
    public int Installments { get; set; }
}

public record BasketCheckoutItemDto(Guid ProductId, string ProductName, string? ImageId, int Quantity, decimal Price);
