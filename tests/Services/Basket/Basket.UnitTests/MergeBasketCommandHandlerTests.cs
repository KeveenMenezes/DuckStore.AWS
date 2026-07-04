using Basket.Function.Modules.ShoppingCarts.Data;
using Basket.Function.Modules.ShoppingCarts.Domain.Entities;
using Basket.Function.Modules.ShoppingCarts.Domain.ValueObjects;
using Basket.Function.Modules.ShoppingCarts.Features.MergeBasket;
using FluentValidation.TestHelper;

namespace Basket.UnitTests;

public class MergeBasketCommandHandlerTests
{
    private const string UserId = "USER#sub-123";
    private const string GuestId = "GUEST#guest-abc";

    private readonly AutoMocker _autoMocker;
    private readonly Mock<IShoppingCartRepository> _basketRepositoryMock;
    private readonly MergeBasketCommandValidator _validator;
    private readonly MergeBasketCommandHandler _handler;

    public MergeBasketCommandHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _basketRepositoryMock = _autoMocker.GetMock<IShoppingCartRepository>();
        _validator = new MergeBasketCommandValidator();
        _handler = _autoMocker.CreateInstance<MergeBasketCommandHandler>();
    }

    [Fact]
    public async Task Handle_ShouldBeNoOp_WhenGuestCartDoesNotExist()
    {
        // Arrange
        _basketRepositoryMock
            .Setup(repo => repo.TryGetBasket(GuestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingCart?)null);

        // Act
        var result = await _handler.Handle(new MergeBasketCommand(UserId, GuestId), CancellationToken.None);

        // Assert
        Assert.Equal(UserId, result.OwnerId);
        _basketRepositoryMock.Verify(repo =>
            repo.MergeAsync(It.IsAny<ShoppingCart>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldCombineQuantities_ForMatchingProducts()
    {
        // Arrange
        var productId = Guid.NewGuid();

        var guestCart = ShoppingCart.Create(GuestId,
            [ShoppingCartItem.Create(ProductId.Of(productId), "IPhone X", "", "Red", 2, 50.0m)]);
        var userCart = ShoppingCart.Create(UserId,
            [ShoppingCartItem.Create(ProductId.Of(productId), "IPhone X", "", "Red", 1, 50.0m)]);

        _basketRepositoryMock
            .Setup(repo => repo.TryGetBasket(GuestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(guestCart);
        _basketRepositoryMock
            .Setup(repo => repo.TryGetBasket(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userCart);

        ShoppingCart? merged = null;
        _basketRepositoryMock
            .Setup(repo => repo.MergeAsync(It.IsAny<ShoppingCart>(), GuestId, It.IsAny<CancellationToken>()))
            .Callback<ShoppingCart, string, CancellationToken>((cart, _, _) => merged = cart)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(new MergeBasketCommand(UserId, GuestId), CancellationToken.None);

        // Assert — one line, quantities summed (no duplicate)
        Assert.NotNull(merged);
        var item = Assert.Single(merged!.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(UserId, merged.OwnerId);
    }

    [Fact]
    public async Task Handle_ShouldCreateUserCart_WhenNoneExists()
    {
        // Arrange
        var guestCart = ShoppingCart.Create(GuestId,
            [ShoppingCartItem.Create(ProductId.Of(Guid.NewGuid()), "IPhone XI", "", "Blue", 1, 40.0m)]);

        _basketRepositoryMock
            .Setup(repo => repo.TryGetBasket(GuestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(guestCart);
        _basketRepositoryMock
            .Setup(repo => repo.TryGetBasket(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingCart?)null);

        ShoppingCart? merged = null;
        _basketRepositoryMock
            .Setup(repo => repo.MergeAsync(It.IsAny<ShoppingCart>(), GuestId, It.IsAny<CancellationToken>()))
            .Callback<ShoppingCart, string, CancellationToken>((cart, _, _) => merged = cart)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(new MergeBasketCommand(UserId, GuestId), CancellationToken.None);

        // Assert
        Assert.NotNull(merged);
        Assert.Equal(UserId, merged!.OwnerId);
        Assert.Single(merged.Items);
    }

    [Theory]
    [InlineData("guest-123", GuestId)]  // OwnerId not a USER# identity
    [InlineData(UserId, "user-456")]    // GuestId not a GUEST# identity
    public void Validator_ShouldHaveError_ForMismatchedPrefixes(string ownerId, string guestId)
    {
        var result = _validator.TestValidate(new MergeBasketCommand(ownerId, guestId));

        Assert.False(result.IsValid);
    }
}
