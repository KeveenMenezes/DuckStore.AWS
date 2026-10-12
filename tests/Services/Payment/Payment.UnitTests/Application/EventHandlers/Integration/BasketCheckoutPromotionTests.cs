using Amazon.DynamoDBv2.Model;
using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.UnitTests.Application.EventHandlers.Integration;

// payment-basket-checkout-consumer promotes the payment the checkout saga registered, and keeps the
// legacy create path while the old checkout pipeline still publishes the event (ADR-0050 §3/§6).
public class BasketCheckoutPromotionTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository = new();

    private CreatePaymentCommand Command(int pointsToUse = 0) =>
        BasketCheckoutMapper.ToCreatePaymentCommand(
            BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent() with { PointsToUse = pointsToUse });

    private PaymentEntity GivenARegisteredPayment(CreatePaymentCommand command)
    {
        var payment = PaymentEntity.Register(
            PaymentId.Of(command.OrderId), command.OrderId, command.CustomerId, command.Amount,
            PaymentDataTests.CreateCardDetails());
        _paymentRepository
            .Setup(r => r.GetByIdAsync(command.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);
        return payment;
    }

    private static Put SinglePut(IReadOnlyList<TransactWriteItem> writes) =>
        Assert.Single(writes).Put;

    [Theory]
    [InlineData(0, "Pending")]
    [InlineData(1000, "AwaitingPoints")]
    public async Task ShouldPromoteTheRegisteredPayment_OnlyIfStillRegistered(int pointsToUse, string promoted)
    {
        var command = Command(pointsToUse);
        GivenARegisteredPayment(command);

        var put = SinglePut(await CreatePaymentHandler.ToPaymentWritesAsync(_paymentRepository.Object, command));

        Assert.Equal(command.OrderId.ToString(), put.Item["Id"].S);
        Assert.Equal(promoted, put.Item["Status"].S);
        // A concurrent void wins: the transaction fails, EventBridge retries, and the retry is a no-op.
        Assert.Equal("#status = :expectedStatus", put.ConditionExpression);
        Assert.Equal("Status", put.ExpressionAttributeNames["#status"]);
        Assert.Equal("Registered", put.ExpressionAttributeValues[":expectedStatus"].S);
    }

    [Theory]
    [InlineData(0, "Pending")]
    [InlineData(1000, "AwaitingPoints")]
    public async Task ShouldCreateThePayment_AsToday_WhenNoneWasRegistered(int pointsToUse, string status)
    {
        var command = Command(pointsToUse);
        _paymentRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentEntity?)null);

        var put = SinglePut(await CreatePaymentHandler.ToPaymentWritesAsync(_paymentRepository.Object, command));

        Assert.Equal(status, put.Item["Status"].S);
        Assert.Equal(command.OrderId.ToString(), put.Item["OrderId"].S);
        Assert.Equal(command.CardNumber, put.Item["Card"].M["CardNumber"].S);
        Assert.Null(put.ConditionExpression);
    }

    // Already promoted (a redelivery the inbox didn't catch) or voided: only the inbox is written.
    [Theory]
    [InlineData("promoted")]
    [InlineData("voided")]
    public async Task ShouldWriteNothing_WhenThePaymentIsNoLongerRegistered(string scenario)
    {
        var command = Command();
        var payment = GivenARegisteredPayment(command);
        if (scenario == "promoted")
            payment.Promote(awaitingPoints: false);
        else
            payment.Void();

        var writes = await CreatePaymentHandler.ToPaymentWritesAsync(_paymentRepository.Object, command);

        Assert.Empty(writes);
    }
}
