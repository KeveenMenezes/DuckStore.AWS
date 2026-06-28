namespace Basket.Function.Data;

public class BasketNotFoundException(string userName)
    : NotFoundException("Basket", userName);
