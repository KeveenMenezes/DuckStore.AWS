namespace Basket.Function.Dtos;

// Wire contract for the StoreBasket request — kept separate from the ShoppingCart aggregate
// so the domain model isn't shaped by (de)serialization concerns.
public record ShoppingCartDto(string UserName, List<ShoppingCartItemDto> Items);

public record ShoppingCartItemDto(int Quantity, string Color, decimal Price, Guid ProductId, string ProductName);
