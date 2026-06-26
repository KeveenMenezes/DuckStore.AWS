namespace Basket.Function.Exceptions;

public class BasketNotFoundException(string userName)
    : NotFoundException("Basket", userName);
