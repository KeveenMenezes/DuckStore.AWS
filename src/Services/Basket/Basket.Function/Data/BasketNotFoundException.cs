namespace Basket.Function.Data;

public class BasketNotFoundException(string ownerId)
    : NotFoundException("Basket", ownerId);
