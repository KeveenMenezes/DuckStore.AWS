namespace Discount.Lambda.Dtos.Requests;

public class UpdateDiscountRequest
{
    public string Id { get; set; }
    public string Code { get; set; }
    public decimal Amount { get; set; }
}
