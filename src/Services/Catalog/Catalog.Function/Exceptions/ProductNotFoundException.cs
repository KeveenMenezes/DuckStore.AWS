namespace Catalog.Function.Exceptions;

public class ProductNotFoundException(Guid Id)
    : NotFoundException("Product", Id)
{
}



