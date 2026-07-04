#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8620 // Argument cannot be used for parameter due to differences in the nullability of reference types.

using Basket.Function.Data;
using Basket.Function.Dtos;
using Basket.Function.Features.CheckoutBasket;
using Basket.Function.Models;
using FluentValidation.TestHelper;

namespace Basket.UnitTests;

public class CheckoutBasketCommandHandlerTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<IBasketRepository> _basketRepositoryMock;
    private readonly CheckoutBasketCommandValidator _validator;
    private readonly CheckoutBasketCommandHandler _handler;

    public CheckoutBasketCommandHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _basketRepositoryMock = _autoMocker.GetMock<IBasketRepository>();
        _validator = new CheckoutBasketCommandValidator();
        _handler = new CheckoutBasketCommandHandler(_basketRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldCheckoutBasketSuccessfully()
    {
        // Arrange
        var basketCheckoutDto = new BasketCheckoutDto
        {
            OwnerId = "USER#testuser",
            TotalPrice = 100.0m
        };

        var basket = ShoppingCart.Create(
            "USER#testuser",
            [ShoppingCartItem.Create(Guid.NewGuid(), "Sample Product", "https://example.com/img.jpg", "Red", 2, 50.0m)]);

        _basketRepositoryMock.Setup(repo =>
            repo.TryGetBasket(basketCheckoutDto.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(basket);

        var command = new CheckoutBasketCommand(basketCheckoutDto);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);

        _basketRepositoryMock.Verify(repo =>
            repo.MarkCheckoutAsync(
                basketCheckoutDto.OwnerId,
                It.Is<string>(json => json.Contains(basketCheckoutDto.OwnerId)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _basketRepositoryMock.Verify(repo =>
            repo.DeleteBasket(basketCheckoutDto.OwnerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailure_WhenBasketDoesNotExist()
    {
        // Arrange
        var basketCheckoutDto = new BasketCheckoutDto
        {
            OwnerId = "USER#testuser",
            TotalPrice = 100.0m
        };

        _basketRepositoryMock
            .Setup(repo =>
                repo.TryGetBasket(basketCheckoutDto.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShoppingCart)null);

        var command = new CheckoutBasketCommand(basketCheckoutDto);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);

        _basketRepositoryMock.Verify(repo =>
            repo.MarkCheckoutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _basketRepositoryMock.Verify(repo =>
            repo.DeleteBasket(basketCheckoutDto.OwnerId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validator_ShouldNotHaveErrors_WhenCommandIsValid()
    {
        // Arrange
        var command = new CheckoutBasketCommand(new BasketCheckoutDto
        {
            OwnerId = "USER#testuser",
            TotalPrice = 100.0m
        });

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.BasketCheckoutDto);
        result.ShouldNotHaveValidationErrorFor(x => x.BasketCheckoutDto.OwnerId);
    }

    [Fact]
    public void Validator_ShouldHaveErrors_WhenCommandIsInvalid()
    {
        // Arrange
        var command = new CheckoutBasketCommand(null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.BasketCheckoutDto)
            .WithErrorMessage("BasketCheckoutDto can't be null");
    }
}
