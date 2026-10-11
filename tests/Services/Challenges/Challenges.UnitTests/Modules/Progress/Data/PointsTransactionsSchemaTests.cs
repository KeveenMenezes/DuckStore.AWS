namespace Challenges.UnitTests.Modules.Progress.Data;

// The TransactionId is the ledger's idempotency key (ADR-0048 §1): one credit per question, one per
// reviewed product, one redemption per order. These pin its exact shape, since a format drift would
// silently let the same source credit twice.
public class PointsTransactionsSchemaTests
{
    [Fact]
    public void ChallengeTransactionId_IsDeterministicPerQuestion()
    {
        var id = PointsTransactionsSchema.ChallengeTransactionId("q-42");

        Assert.Equal("CHALLENGE#q-42", id);
        Assert.Equal(id, PointsTransactionsSchema.ChallengeTransactionId("q-42"));
    }

    [Fact]
    public void ReviewTransactionId_IsDeterministicPerProduct()
    {
        var productId = Guid.NewGuid();

        var id = PointsTransactionsSchema.ReviewTransactionId(productId);

        Assert.Equal($"REVIEW#{productId}", id);
    }

    [Fact]
    public void RedemptionTransactionId_IsDeterministicPerOrder()
    {
        var orderId = Guid.NewGuid();

        var id = PointsTransactionsSchema.RedemptionTransactionId(orderId);

        Assert.Equal($"REDEMPTION#{orderId}", id);
    }

    [Theory]
    [InlineData("CHALLENGE#q-42", "q-42")]
    [InlineData("REVIEW#abc", "abc")]
    [InlineData("REDEMPTION#ord-1", "ord-1")]
    public void ParseSourceId_StripsTheTypePrefix(string transactionId, string expected)
    {
        Assert.Equal(expected, PointsTransactionsSchema.ParseSourceId(transactionId));
    }

    [Fact]
    public void ParseSourceId_RejectsAnUnknownPrefix()
    {
        Assert.Throws<ArgumentException>(() => PointsTransactionsSchema.ParseSourceId("ATTEMPT#q-1"));
    }
}
