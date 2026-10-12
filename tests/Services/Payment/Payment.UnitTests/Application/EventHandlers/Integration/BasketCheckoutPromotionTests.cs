using Amazon.DynamoDBv2.Model;
using PaymentEntity = Payment.Function.Modules.Payments.Domain.Entities.Payment;

namespace Payment.UnitTests.Application.EventHandlers.Integration;

// payment-basket-checkout-consumer promotes the payment the checkout saga registered (ADR-0050 §6).
public class BasketCheckoutPromotionTests
{
    private readonly Mock<IPaymentRepository> _paymentRepository = new();

    private static PromotePaymentCommand Command(int pointsToUse = 0) =>
        BasketCheckoutMapper.ToPromotePaymentCommand(
            BasketCheckoutEventDataTests.CreateValidBasketCheckoutEvent() with { PointsToUse = pointsToUse });

    private PaymentEntity GivenARegisteredPayment(PromotePaymentCommand command)
    {
        var payment = PaymentEntity.Register(
            PaymentId.Of(command.OrderId), command.OrderId, Guid.NewGuid(), 100m,
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

        var put = SinglePut(await PromotePaymentHandler.ToPaymentWritesAsync(_paymentRepository.Object, command));

        Assert.Equal(command.OrderId.ToString(), put.Item["Id"].S);
        Assert.Equal(promoted, put.Item["Status"].S);
        // A concurrent void wins: the transaction fails, EventBridge retries, and the retry is a no-op.
        Assert.Equal("#status = :expectedStatus", put.ConditionExpression);
        Assert.Equal("Status", put.ExpressionAttributeNames["#status"]);
        Assert.Equal("Registered", put.ExpressionAttributeValues[":expectedStatus"].S);
    }

    // Every checkout registers the payment before the cart closes, so a missing one is retried
    // (and lands in the DLQ if it never appears) rather than created from the event.
    [Fact]
    public async Task ShouldThrow_WhenNoPaymentWasRegistered()
    {
        var command = Command();
        _paymentRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentEntity?)null);

        await Assert.ThrowsAsync<PaymentForOrderNotFoundException>(() =>
            PromotePaymentHandler.ToPaymentWritesAsync(_paymentRepository.Object, command));
    }

    // Already promoted (a redelivery the inbox didn't catch): only the inbox is written.
    [Fact]
    public async Task ShouldWriteNothing_WhenThePaymentWasAlreadyPromoted()
    {
        var command = Command();
        GivenARegisteredPayment(command).Promote(awaitingPoints: false);

        var writes = await PromotePaymentHandler.ToPaymentWritesAsync(_paymentRepository.Object, command);

        Assert.Empty(writes);
    }

    // Two executions of one checkout attempt: A's CartChanged voided the payment just before B
    // closed the cart. The event proves the cart closed under this OrderId, so the payment is
    // promoted anyway — conditioned on the Voided status it was read in.
    [Fact]
    public async Task ShouldPromoteAVoidedPayment_BecauseTheEventProvesTheCartClosed()
    {
        var command = Command();
        GivenARegisteredPayment(command).Void();

        var put = SinglePut(await PromotePaymentHandler.ToPaymentWritesAsync(_paymentRepository.Object, command));

        Assert.Equal("Pending", put.Item["Status"].S);
        Assert.Equal("Voided", put.ExpressionAttributeValues[":expectedStatus"].S);
    }
}
