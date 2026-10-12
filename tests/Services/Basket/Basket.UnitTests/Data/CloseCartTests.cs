using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Basket.Function.Modules.ShoppingCarts.Data;

namespace Basket.UnitTests.Data;

// The cart closes in one conditional transaction (ADR-0050 §4): a CHECKOUT# record that exists at
// most once per attempt, and a Delete of the cart that only succeeds on the quoted version.
public class CloseCartTests
{
    private const string OwnerId = "USER#sub-123";
    private const string CheckoutId = "chk-1";
    private const string CheckoutKey = "CHECKOUT#USER#sub-123#chk-1";

    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoShoppingCartRepository _repository;
    private TransactWriteItemsRequest? _sent;

    public CloseCartTests() => _repository = new DynamoShoppingCartRepository(_dynamoDb.Object);

    private void TransactionSucceeds() =>
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => _sent = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

    private void TransactionCancelled(string putCode, string deleteCode) =>
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons = [new CancellationReason { Code = putCode }, new CancellationReason { Code = deleteCode }]
            });

    [Fact]
    public async Task CloseCartAsync_ShouldPutTheCheckoutRecordAndDeleteTheQuotedVersion_InOneTransaction()
    {
        TransactionSucceeds();
        var orderId = Guid.NewGuid();

        var result = await _repository.CloseCartAsync(OwnerId, CheckoutId, "v1", orderId, "{}", CancellationToken.None);

        Assert.Equal(new CartCloseResult(CartCloseOutcome.Closed, orderId), result);
        var put = Assert.Single(_sent!.TransactItems, i => i.Put is not null).Put;
        Assert.Equal(CheckoutKey, put.Item["OwnerId"].S);
        Assert.Equal("Checkout", put.Item["Type"].S);
        Assert.Equal(orderId.ToString(), put.Item["OrderId"].S);
        Assert.True(put.Item.ContainsKey("ExpiresAt"));
        Assert.Equal("attribute_not_exists(OwnerId)", put.ConditionExpression);

        var delete = Assert.Single(_sent.TransactItems, i => i.Delete is not null).Delete;
        Assert.Equal(OwnerId, delete.Key["OwnerId"].S);
        Assert.Equal("#version = :version", delete.ConditionExpression);
        Assert.Equal("v1", delete.ExpressionAttributeValues[":version"].S);
    }

    [Fact]
    public async Task CloseCartAsync_ShouldRequireTheCartToExist_WhenThePreVersionCartHadNoVersion()
    {
        // The bare "Version = :v OR attribute_not_exists(Version)" would also pass on a cart that a
        // different attempt already deleted — closing one cart twice. Both branches must fail on a
        // missing item.
        TransactionSucceeds();

        await _repository.CloseCartAsync(OwnerId, CheckoutId, cartVersion: null, Guid.NewGuid(), "{}", CancellationToken.None);

        var delete = Assert.Single(_sent!.TransactItems, i => i.Delete is not null).Delete;
        Assert.Equal("attribute_exists(OwnerId) AND attribute_not_exists(#version)", delete.ConditionExpression);
    }

    [Fact]
    public async Task CloseCartAsync_ShouldReturnTheStoredOrderId_WhenTheCheckoutRecordAlreadyExists()
    {
        // A failed Put means this attempt already closed the cart — it takes precedence over the
        // Delete failing too (the cart is gone precisely because of that first close).
        TransactionCancelled(putCode: "ConditionalCheckFailed", deleteCode: "ConditionalCheckFailed");
        var storedOrderId = Guid.NewGuid();
        _dynamoDb
            .Setup(d => d.GetItemAsync(
                It.Is<GetItemRequest>(r => r.Key["OwnerId"].S == CheckoutKey && r.ConsistentRead == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue> { ["OrderId"] = new(storedOrderId.ToString()) }
            });

        var result = await _repository.CloseCartAsync(OwnerId, CheckoutId, "v1", Guid.NewGuid(), "{}", CancellationToken.None);

        Assert.Equal(new CartCloseResult(CartCloseOutcome.Replayed, storedOrderId), result);
    }

    [Fact]
    public async Task CloseCartAsync_ShouldReportCartChanged_WhenOnlyTheVersionConditionFails()
    {
        TransactionCancelled(putCode: "None", deleteCode: "ConditionalCheckFailed");

        var result = await _repository.CloseCartAsync(OwnerId, CheckoutId, "v1", Guid.NewGuid(), "{}", CancellationToken.None);

        Assert.Equal(CartCloseOutcome.CartChanged, result.Outcome);
    }

    [Fact]
    public async Task CloseCartAsync_ShouldRethrow_WhenTheTransactionFailsForAnyOtherReason()
    {
        // Not a typed outcome: the saga treats it as ambiguous and never compensates (ADR-0050 §1).
        TransactionCancelled(putCode: "TransactionConflict", deleteCode: "None");

        await Assert.ThrowsAsync<TransactionCanceledException>(() =>
            _repository.CloseCartAsync(OwnerId, CheckoutId, "v1", Guid.NewGuid(), "{}", CancellationToken.None));
    }
}
