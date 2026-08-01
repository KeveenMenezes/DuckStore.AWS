namespace Pricing.Function.Modules.CustomerDiscounts.Data;

public interface ICustomerDiscountRepository
{
    // Null when no such discount exists for this owner — GetBasketInstallmentPlan (CH-13) treats
    // that the same as "not eligible," never distinguishing "wrong owner" from "doesn't exist" in
    // its error (no oracle for whether a discountId belongs to someone else).
    Task<CustomerDiscount?> GetAsync(
        string ownerId, string discountId, CancellationToken cancellationToken = default);
}
