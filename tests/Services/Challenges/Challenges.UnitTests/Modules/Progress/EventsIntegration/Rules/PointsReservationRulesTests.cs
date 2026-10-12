using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Challenges.Function.Modules.Progress.EventsIntegration.Publishers.PointsTransactions;
using Challenges.Function.Modules.Progress.EventsIntegration.Publishers.PointsTransactions.Rules;

namespace Challenges.UnitTests.Modules.Progress.EventsIntegration.Rules;

// The points-transactions stream rules (ADR-0048 §5): Payment learns whether the checkout's points
// were reserved from the ledger row's INSERT, never from money — the events carry a point quantity.
public class PointsReservationRulesTests
{
    private static readonly Guid OrderId = Guid.Parse("b91258ab-9298-43ae-9ee0-b65752590ec4");

    private static PointsTransactionStreamImage Row(string status, string? transactionId = null, string? orderId = null) =>
        new("USER#alice", transactionId ?? $"REDEMPTION#{OrderId}", status, -1000, orderId ?? OrderId.ToString());

    private static StreamContext<PointsTransactionStreamImage> Insert(PointsTransactionStreamImage row) =>
        new("INSERT", null, row);

    [Fact]
    public void Reserved_ShouldMatch_TheInsertOfAReservedRedemption() =>
        Assert.True(new PointsReservedRule().Match(Insert(Row("Reserved"))));

    [Fact]
    public void Failed_ShouldMatch_TheInsertOfAFailedRedemption() =>
        Assert.True(new PointsReservationFailedRule().Match(Insert(Row("Failed"))));

    [Fact]
    public void Reserved_ShouldNotMatch_AFailedRow() =>
        Assert.False(new PointsReservedRule().Match(Insert(Row("Failed"))));

    [Fact]
    public void Failed_ShouldNotMatch_AReservedRow() =>
        Assert.False(new PointsReservationFailedRule().Match(Insert(Row("Reserved"))));

    // Settlement moves Reserved -> Used/Released by MODIFY; that must not announce a new reservation.
    [Theory]
    [InlineData("Used")]
    [InlineData("Released")]
    [InlineData("Reserved")]
    public void Reserved_ShouldNotMatch_AModify(string newStatus)
    {
        var context = new StreamContext<PointsTransactionStreamImage>("MODIFY", Row("Reserved"), Row(newStatus));

        Assert.False(new PointsReservedRule().Match(context));
    }

    [Fact]
    public void Rules_ShouldNotMatch_ACredit()
    {
        var credit = Row("Completed", transactionId: "CHALLENGE#py-001", orderId: null) with { OrderId = null, Points = 75 };

        Assert.False(new PointsReservedRule().Match(Insert(credit)));
        Assert.False(new PointsReservationFailedRule().Match(Insert(credit)));
    }

    [Fact]
    public async Task Reserved_ShouldPublishThePointsAsAPositiveQuantity()
    {
        var instruction = await new PointsReservedRule().BuildAsync(Insert(Row("Reserved")));

        Assert.Equal(nameof(PointsReservedEvent), instruction.DetailType);
        var payload = Assert.IsType<PointsReservedEvent>(instruction.Payload);
        Assert.Equal(OrderId, payload.OrderId);
        Assert.Equal("USER#alice", payload.OwnerId);
        Assert.Equal(1000, payload.Points);
    }

    [Fact]
    public async Task Failed_ShouldPublishThePointsAsked()
    {
        var instruction = await new PointsReservationFailedRule().BuildAsync(Insert(Row("Failed")));

        Assert.Equal(nameof(PointsReservationFailedEvent), instruction.DetailType);
        var payload = Assert.IsType<PointsReservationFailedEvent>(instruction.Payload);
        Assert.Equal(OrderId, payload.OrderId);
        Assert.Equal("USER#alice", payload.OwnerId);
        Assert.Equal(1000, payload.Points);
    }
}
