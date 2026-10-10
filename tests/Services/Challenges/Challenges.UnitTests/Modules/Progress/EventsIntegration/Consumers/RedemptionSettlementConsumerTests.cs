using BuildingBlocks.Messaging.Events;
using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.OrderCancelled;
using Challenges.Function.Modules.Progress.EventsIntegration.Consumers.PaymentAuthorized;

namespace Challenges.UnitTests.Modules.Progress.EventsIntegration.Consumers;

// Settling the checkout's points (ADR-0048 §5): authorized → Used; order cancelled → Released or
// Refunded with the points back. Every order's payment and cancellation reach these consumers, so
// an order with no redemption row is simply not theirs.
public class RedemptionSettlementConsumerTests
{
    private static readonly Guid CustomerId = Guid.Parse("6428f448-0000-4000-8000-000000000001");
    private static readonly Guid OrderId = Guid.Parse("b91258ab-9298-43ae-9ee0-b65752590ec4");
    private static readonly OwnerId Owner = OwnerId.Of($"USER#{CustomerId}");

    private readonly Mock<IPlayerProgressRepository> _repository = new();

    private PointsTransaction StoredRedemption(PointsTransactionStatus status)
    {
        var redemption = PointsTransaction.Load(
            Owner, $"REDEMPTION#{OrderId}", PointsTransactionType.Redemption, status, -1000,
            OrderId.ToString(), OrderId.ToString(), DateTime.UtcNow, DateTime.UtcNow);
        _repository
            .Setup(r => r.GetRedemptionAsync(Owner, OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(redemption);
        _repository
            .Setup(r => r.SaveSettlementAsync(It.IsAny<PointsTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return redemption;
    }

    private void NoRedemption() =>
        _repository
            .Setup(r => r.GetRedemptionAsync(It.IsAny<OwnerId>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PointsTransaction?)null);

    private Task<bool> Authorize() =>
        new PaymentAuthorizedHandler(_repository.Object).HandleAsync(
            PaymentAuthorizedMapper.ToCommand(new PaymentAuthorizedEvent { OrderId = OrderId, CustomerId = CustomerId }));

    private Task<bool> Cancel() =>
        new OrderCancelledHandler(_repository.Object).HandleAsync(
            OrderCancelledMapper.ToCommand(new OrderCancelledEvent { OrderId = OrderId, CustomerId = CustomerId }));

    [Fact]
    public async Task PaymentAuthorized_ShouldMarkTheReservationUsed()
    {
        var redemption = StoredRedemption(PointsTransactionStatus.Reserved);

        Assert.True(await Authorize());
        Assert.Equal(PointsTransactionStatus.Used, redemption.Status);
        _repository.Verify(r => r.SaveSettlementAsync(redemption, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PaymentAuthorized_ShouldBeANoOp_ForAnOrderWithoutPoints()
    {
        NoRedemption();

        Assert.False(await Authorize());
        _repository.Verify(
            r => r.SaveSettlementAsync(It.IsAny<PointsTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PaymentAuthorized_ShouldBeANoOp_WhenAlreadyUsed()
    {
        StoredRedemption(PointsTransactionStatus.Used);

        Assert.False(await Authorize());
        _repository.Verify(
            r => r.SaveSettlementAsync(It.IsAny<PointsTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(PointsTransactionStatus.Reserved, PointsTransactionStatus.Released)]
    [InlineData(PointsTransactionStatus.Used, PointsTransactionStatus.Refunded)]
    public async Task OrderCancelled_ShouldGiveThePointsBack(PointsTransactionStatus from, PointsTransactionStatus to)
    {
        var redemption = StoredRedemption(from);

        Assert.True(await Cancel());
        Assert.Equal(to, redemption.Status);
        Assert.Equal(1000, redemption.PointsToReturn);
    }

    [Fact]
    public async Task OrderCancelled_ShouldBeANoOp_ForAFailedReservation()
    {
        StoredRedemption(PointsTransactionStatus.Failed);

        Assert.False(await Cancel());
        _repository.Verify(
            r => r.SaveSettlementAsync(It.IsAny<PointsTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrderCancelled_ShouldBeANoOp_ForAnOrderWithoutPoints()
    {
        NoRedemption();

        Assert.False(await Cancel());
    }

    [Fact]
    public void Mappers_ShouldReject_AnEventWithoutTheCustomerOrOrder()
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.ValidationException>(
            () => PaymentAuthorizedMapper.ToCommand(new PaymentAuthorizedEvent { OrderId = OrderId }));
        Assert.Throws<BuildingBlocks.Core.Exceptions.ValidationException>(
            () => OrderCancelledMapper.ToCommand(new OrderCancelledEvent { CustomerId = CustomerId }));
    }
}
