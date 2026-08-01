using Pricing.Function.Modules.CustomerDiscounts.EventsIntegration.Consumers.PaymentAuthorized;

namespace Pricing.UnitTests.Application.EventHandlers.Integration;

public class PaymentAuthorizedMapperTests
{
    [Fact]
    public void ToOwnerId_ShouldPrefixTheCustomerIdWithUser()
    {
        var customerId = Guid.NewGuid();

        var ownerId = PaymentAuthorizedMapper.ToOwnerId(customerId);

        Assert.Equal($"USER#{customerId}", ownerId);
    }
}

public class PaymentAuthorizedHandlerTests
{
    [Fact]
    public void BuildConsumeTransactItems_ShouldConditionallyFlipStatus_OnTheOwnersDiscountRow()
    {
        var items = PaymentAuthorizedHandler.BuildConsumeTransactItems("USER#alice", "discount-1");

        Assert.Single(items);
        Assert.Equal("customer-discounts", items[0].Update.TableName);
        Assert.Equal("USER#alice", items[0].Update.Key["OwnerId"].S);
        Assert.Equal("discount-1", items[0].Update.Key["DiscountId"].S);
        Assert.Equal("#status = :issued", items[0].Update.ConditionExpression);
    }
}
