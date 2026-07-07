using Basket.Function.Modules.ShoppingCarts.Data;
using Basket.Function.Modules.ShoppingCarts.Domain.Dtos;
using Basket.Function.Modules.ShoppingCarts.Domain.Entities;
using Basket.Function.Modules.ShoppingCarts.Features.StoreBasket;
using FluentValidation.TestHelper;

namespace Basket.UnitTests;

public class StoreBasketCommandHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IShoppingCartRepository> _basketRepositoryMock;
    private readonly StoreBasketCommandValidator _validator;
    private readonly StoreBasketCommandHandler _handler;

    public StoreBasketCommandHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _basketRepositoryMock = _autoMocker.GetMock<IShoppingCartRepository>();
        _validator = new StoreBasketCommandValidator();
        _handler = _autoMocker.CreateInstance<StoreBasketCommandHandler>();
    }

    [Fact]
    public async Task Handle_ShouldStoreCart_WithItemPriceAsSubmitted()
    {
        // Arrange — Basket no longer resolves discounts (moved to Pricing, ADR-0026); the price
        // it stores is whatever the caller submits.
        var command = new StoreBasketCommand(new ShoppingCartDto(
            "USER#testuser",
            [new ShoppingCartItemDto(2, "Red", 50.0m, Guid.NewGuid(), "IPhone X", "")]));

        ShoppingCart? storedCart = null;
        _basketRepositoryMock
            .Setup(repo => repo.StoreCart(It.IsAny<ShoppingCart>(), It.IsAny<CancellationToken>()))
            .Callback<ShoppingCart, CancellationToken>((cart, _) => storedCart = cart)
            .ReturnsAsync((ShoppingCart cart, CancellationToken _) => cart);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal("USER#testuser", result.OwnerId);
        Assert.NotNull(storedCart);
        Assert.Equal(50.0m, storedCart!.Items.Single().Price);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOwnerIdIsEmpty()
    {
        var command = new StoreBasketCommand(new ShoppingCartDto(string.Empty, []));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Cart.OwnerId);
    }
}
