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
    private readonly Mock<ICouponRepository> _couponRepositoryMock;
    private readonly StoreBasketCommandValidator _validator;
    private readonly StoreBasketCommandHandler _handler;

    public StoreBasketCommandHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _basketRepositoryMock = _autoMocker.GetMock<IShoppingCartRepository>();
        _couponRepositoryMock = _autoMocker.GetMock<ICouponRepository>();
        _validator = new StoreBasketCommandValidator();
        _handler = _autoMocker.CreateInstance<StoreBasketCommandHandler>();
    }

    [Fact]
    public async Task Handle_ShouldDeductCouponAmount_FromMatchingItemPrice()
    {
        // Arrange
        var command = new StoreBasketCommand(new ShoppingCartDto(
            "USER#testuser",
            [new ShoppingCartItemDto(2, "Red", 50.0m, Guid.NewGuid(), "IPhone X", "")]));

        _couponRepositoryMock
            .Setup(repo => repo.GetByProductNameAsync("IPhone X", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coupon { Id = "IPhone X", Description = "10 off", Amount = 10 });

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
        Assert.Equal(40.0m, storedCart!.Items.Single().Price);
    }

    [Fact]
    public async Task Handle_ShouldLeavePriceUnchanged_WhenNoCouponExists()
    {
        // Arrange
        var command = new StoreBasketCommand(new ShoppingCartDto(
            "USER#testuser",
            [new ShoppingCartItemDto(1, "Blue", 30.0m, Guid.NewGuid(), "Unknown Product", "")]));

        _couponRepositoryMock
            .Setup(repo => repo.GetByProductNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Coupon?)null);

        ShoppingCart? storedCart = null;
        _basketRepositoryMock
            .Setup(repo => repo.StoreCart(It.IsAny<ShoppingCart>(), It.IsAny<CancellationToken>()))
            .Callback<ShoppingCart, CancellationToken>((cart, _) => storedCart = cart)
            .ReturnsAsync((ShoppingCart cart, CancellationToken _) => cart);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(storedCart);
        Assert.Equal(30.0m, storedCart!.Items.Single().Price);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenOwnerIdIsEmpty()
    {
        var command = new StoreBasketCommand(new ShoppingCartDto(string.Empty, []));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Cart.OwnerId);
    }
}
