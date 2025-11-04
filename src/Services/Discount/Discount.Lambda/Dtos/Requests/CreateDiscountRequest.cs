namespace Discount.Lambda.Dtos.Requests;

public class CreateDiscountRequest
{
    public string Code { get; set; }
    public decimal Amount { get; set; }
}
