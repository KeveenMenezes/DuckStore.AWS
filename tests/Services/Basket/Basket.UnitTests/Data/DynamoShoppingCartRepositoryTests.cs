using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Basket.Function.Modules.ShoppingCarts.Data;
using Basket.Function.Modules.ShoppingCarts.Domain.Entities;
using Basket.Function.Modules.ShoppingCarts.Domain.ValueObjects;

namespace Basket.UnitTests.Data;

public class DynamoShoppingCartRepositoryTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly List<TransactWriteItemsRequest> _transactions = [];

    public DynamoShoppingCartRepositoryTests() =>
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((request, _) => _transactions.Add(request))
            .ReturnsAsync(new TransactWriteItemsResponse());

    private static ShoppingCart UserCart() =>
        ShoppingCart.Create(
            "USER#sub-123",
            [ShoppingCartItem.Create(ProductId.Of(Guid.NewGuid()), "Duck", null, "Yellow", 1, 10m)]);

    [Fact]
    public async Task MergeAsync_ShouldWriteANewCartVersion_OnEveryWrite()
    {
        // The checkout closes the cart only if its Version is still the one Pricing quoted
        // (ADR-0050 §4), so every write that changes the cart must mint a fresh one.
        var repository = new DynamoShoppingCartRepository(_dynamoDb.Object);

        await repository.MergeAsync(UserCart(), "GUEST#guest-abc", CancellationToken.None);
        await repository.MergeAsync(UserCart(), "GUEST#guest-abc", CancellationToken.None);

        var versions = _transactions
            .Select(t => t.TransactItems.Single(i => i.Put is not null).Put.Item["Version"].S)
            .ToList();
        Assert.All(versions, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.NotEqual(versions[0], versions[1]);
    }
}
