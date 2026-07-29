namespace Pricing.Function.Modules.Campaigns.Domain.ValueObjects;

public class CategoryId : ValueObject<Guid>
{
    private CategoryId(Guid value) : base(value) { }

    public static CategoryId Of(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new CategoryIdBadRequestException(value);
        }

        return new CategoryId(value);
    }
}
