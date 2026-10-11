using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Review.Function.Modules.Reviews.Data;
using Review.Function.Modules.Reviews.Domain.Enums;
using Review.Function.Modules.Reviews.EventsIntegration.Consumers.OrderCompleted;

namespace Review.UnitTests.EventsIntegration.Consumers;

public class OrderCompletedHandlerTests
{
    private readonly AutoMocker _mocker = new();
    private readonly List<PutItemRequest> _puts = [];

    public OrderCompletedHandlerTests()
    {
        // Real repository over a mocked client: the assertions are on the exact PutItem shape.
        _mocker.Use<IReviewEligibilityRepository>(
            new DynamoReviewEligibilityRepository(_mocker.Get<IAmazonDynamoDB>()));
    }

    private void SetupPutItem(Func<PutItemRequest, Task<PutItemResponse>> behaviour) =>
        _mocker.GetMock<IAmazonDynamoDB>()
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Returns((PutItemRequest request, CancellationToken _) =>
            {
                _puts.Add(request);
                return behaviour(request);
            });

    [Fact]
    public async Task HandleAsync_WritesOneConditionalEligiblePutItemPerProduct_WithoutGsi1()
    {
        SetupPutItem(_ => Task.FromResult(new PutItemResponse()));
        var customerId = Guid.NewGuid();
        List<Guid> productIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var handler = _mocker.CreateInstance<OrderCompletedHandler>();

        var created = await handler.HandleAsync(new MarkReviewsEligibleCommand(customerId, productIds));

        Assert.Equal(3, created);
        Assert.Equal(productIds.Count, _puts.Count);
        _mocker.GetMock<IAmazonDynamoDB>().Verify(
            d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        for (var i = 0; i < productIds.Count; i++)
        {
            var put = _puts[i];
            var productId = productIds[i].ToString();

            Assert.Equal(ReviewSchema.TableName, put.TableName);
            Assert.Equal("attribute_not_exists(Id)", put.ConditionExpression);
            Assert.Equal(ReviewSchema.ComposeId(productId, customerId.ToString()), put.Item["Id"].S);
            Assert.Equal(productId, put.Item["ProductId"].S);
            Assert.Equal(customerId.ToString(), put.Item["UserId"].S);
            Assert.Equal(ReviewStatus.Eligible.ToString(), put.Item[ReviewSchema.StatusAttribute].S);
            Assert.False(put.Item.ContainsKey(ReviewSchema.Gsi1PkAttribute));
            Assert.False(put.Item.ContainsKey(ReviewSchema.Gsi1SkAttribute));
            Assert.False(put.Item.ContainsKey("Rating"));
            Assert.False(put.Item.ContainsKey("Comment"));
        }
    }

    [Fact]
    public async Task HandleAsync_WritesCreatedAtInTheAppSyncIso8601MillisecondFormat()
    {
        SetupPutItem(_ => Task.FromResult(new PutItemResponse()));
        var handler = _mocker.CreateInstance<OrderCompletedHandler>();

        await handler.HandleAsync(new MarkReviewsEligibleCommand(Guid.NewGuid(), [Guid.NewGuid()]));

        var createdAt = Assert.Single(_puts).Item["CreatedAt"].S;
        // Same shape as util.time.nowISO8601() (createReview copies CreatedAt into GSI1SK), so
        // reviewsByProduct sorts rows from both writers consistently.
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", createdAt);
    }

    [Fact]
    public async Task HandleAsync_ConditionalCheckFailedOnOneProduct_IsNoOp_AndRemainingProductsAreWritten()
    {
        var alreadyReviewed = Guid.NewGuid();
        SetupPutItem(request => request.Item["ProductId"].S == alreadyReviewed.ToString()
            ? throw new ConditionalCheckFailedException("row already exists")
            : Task.FromResult(new PutItemResponse()));
        List<Guid> productIds = [Guid.NewGuid(), alreadyReviewed, Guid.NewGuid()];
        var handler = _mocker.CreateInstance<OrderCompletedHandler>();

        var created = await handler.HandleAsync(new MarkReviewsEligibleCommand(Guid.NewGuid(), productIds));

        Assert.Equal(2, created);
        Assert.Equal(
            productIds.Select(id => id.ToString()),
            _puts.Select(put => put.Item["ProductId"].S));
    }

    [Fact]
    public async Task HandleAsync_OtherDynamoFailure_Propagates()
    {
        SetupPutItem(_ => throw new ProvisionedThroughputExceededException("throttled"));
        var handler = _mocker.CreateInstance<OrderCompletedHandler>();

        await Assert.ThrowsAsync<ProvisionedThroughputExceededException>(() =>
            handler.HandleAsync(new MarkReviewsEligibleCommand(Guid.NewGuid(), [Guid.NewGuid()])));
    }
}
