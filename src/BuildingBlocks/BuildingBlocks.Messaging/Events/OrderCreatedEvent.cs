namespace BuildingBlocks.Messaging.Events;

public record OrderCreatedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public string OrderName { get; init; } = default!;
    public int Status { get; init; }

    // Shipping address
    public string FirstName { get; init; } = default!;
    public string LastName { get; init; } = default!;
    public string? EmailAddress { get; init; }
    public string AddressLine { get; init; } = default!;
    public string Country { get; init; } = default!;
    public string State { get; init; } = default!;
    public string ZipCode { get; init; } = default!;

    // Payment
    public string? CardName { get; init; }
    public string CardNumber { get; init; } = default!;
    public string Expiration { get; init; } = default!;
    public string Cvv { get; init; } = default!;
    public int PaymentMethod { get; init; }

    public List<OrderCreatedItem> Items { get; init; } = [];
}

public record OrderCreatedItem(Guid ProductId, int Quantity, decimal Price);
