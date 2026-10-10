namespace Payment.UnitTests.Application.EventHandlers.Integration;

public class BasketCheckoutMapperTests
{
    [Fact]
    public void ToCreatePaymentCommand_ShouldMapFields_WhenEventIsValid()
    {
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent();

        var command = BasketCheckoutMapper.ToCreatePaymentCommand(basketCheckoutEvent);

        Assert.Equal(basketCheckoutEvent.OrderId, command.OrderId);
        Assert.Equal(basketCheckoutEvent.CustomerId, command.CustomerId);
        Assert.Equal(basketCheckoutEvent.Payment.CardNumber, command.CardNumber);
        Assert.Equal(basketCheckoutEvent.TotalPrice, command.Amount);
    }

    [Fact]
    public void ToCreatePaymentCommand_ShouldCarryTheDiscountId_WhenACouponWasApplied()
    {
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent() with
        {
            DiscountId = "discount-1"
        };

        var command = BasketCheckoutMapper.ToCreatePaymentCommand(basketCheckoutEvent);

        Assert.Equal("discount-1", command.DiscountId);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1000, true)]
    public async Task ToCreatePaymentCommand_ShouldAwaitPoints_OnlyWhenTheCheckoutUsedThem(int pointsToUse, bool awaiting)
    {
        var basketCheckoutEvent = BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent() with
        {
            PointsToUse = pointsToUse
        };
        var repository = new Mock<IPaymentRepository>();
        Payment.Function.Modules.Payments.Domain.Entities.Payment? created = null;
        repository
            .Setup(r => r.AddAsync(It.IsAny<Payment.Function.Modules.Payments.Domain.Entities.Payment>(), It.IsAny<CancellationToken>()))
            .Callback<Payment.Function.Modules.Payments.Domain.Entities.Payment, CancellationToken>((p, _) => created = p)
            .Returns(Task.CompletedTask);

        var command = BasketCheckoutMapper.ToCreatePaymentCommand(basketCheckoutEvent);
        await new CreatePaymentHandler(repository.Object).Handle(command, CancellationToken.None);

        Assert.Equal(pointsToUse, command.PointsToUse);
        Assert.Equal(awaiting ? PaymentStatus.AwaitingPoints : PaymentStatus.Pending, created!.Status);
    }
}
