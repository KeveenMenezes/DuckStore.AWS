using BuildingBlocks.Messaging.Events;
using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.BasketCheckout;

namespace Challenges.UnitTests.Modules.Progress.EventsIntegration.Consumers;

public class BasketCheckoutConsumerTests
{
    private static readonly Guid OrderId = Guid.Parse("b91258ab-9298-43ae-9ee0-b65752590ec4");

    private static BasketCheckoutEvent Checkout(int pointsToUse, string ownerId = "USER#alice", Guid? orderId = null) =>
        new() { OwnerId = ownerId, OrderId = orderId ?? OrderId, PointsToUse = pointsToUse, TotalPrice = 190m };

    [Fact]
    public void Mapper_ShouldIgnore_ACheckoutWithoutPoints()
    {
        // Every checkout reaches this consumer; most use no points and are none of its business.
        Assert.Null(BasketCheckoutMapper.ToCommand(Checkout(pointsToUse: 0)));
    }

    [Fact]
    public void Mapper_ShouldMapTheOwnerOrderAndPoints()
    {
        var command = BasketCheckoutMapper.ToCommand(Checkout(pointsToUse: 1000));

        Assert.NotNull(command);
        Assert.Equal(OwnerId.Of("USER#alice"), command!.OwnerId);
        Assert.Equal(OrderId, command.OrderId);
        Assert.Equal(1000, command.Points);
    }

    [Fact]
    public void Mapper_ShouldReject_PointsWithoutAnOrder()
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.ValidationException>(
            () => BasketCheckoutMapper.ToCommand(Checkout(pointsToUse: 1000, orderId: Guid.Empty)));
    }

    [Fact]
    public void Mapper_ShouldReject_PointsFromAGuestCart()
    {
        // Checkout is Cognito-only and guests never score (ADR-0045 §7): a GUEST# owner with points
        // is a malformed event, sent to the DLQ rather than reserved against nobody.
        Assert.Throws<OwnerIdBadRequestException>(
            () => BasketCheckoutMapper.ToCommand(Checkout(pointsToUse: 1000, ownerId: "GUEST#abc")));
    }

    [Theory]
    [InlineData(PointsReservationOutcome.Reserved)]
    [InlineData(PointsReservationOutcome.Failed)]
    [InlineData(PointsReservationOutcome.AlreadyProcessed)]
    public async Task Handler_ShouldReserveThroughTheRepository_AndReturnItsOutcome(PointsReservationOutcome outcome)
    {
        var repository = new Mock<IPlayerProgressRepository>();
        repository
            .Setup(r => r.ReservePointsAsync(OwnerId.Of("USER#alice"), OrderId, 1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

        var result = await new BasketCheckoutHandler(repository.Object)
            .HandleAsync(new ReservePointsCommand(OwnerId.Of("USER#alice"), OrderId, 1000));

        Assert.Equal(outcome, result);
    }
}
