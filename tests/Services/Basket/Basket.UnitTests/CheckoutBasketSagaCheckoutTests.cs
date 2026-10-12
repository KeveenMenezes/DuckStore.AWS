using Basket.Function;
using Basket.Function.Modules.ShoppingCarts.Data;
using Basket.Function.Modules.ShoppingCarts.Domain.Dtos;
using Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

namespace Basket.UnitTests;

// The checkout as the saga calls it (ADR-0050 §4): the saga already derived the OrderId and priced
// and named the lines, so Basket closes the cart atomically and never re-reads it.
public class CheckoutBasketSagaCheckoutTests
{
    private const string OwnerId = "USER#testuser";
    private const string CheckoutId = "7d0f5c3e-1a2b-4c3d-8e9f-0a1b2c3d4e5f";
    private const string CartVersion = "v1";

    private readonly Mock<IShoppingCartRepository> _repository = new();
    private readonly CheckoutBasketCommandHandler _handler;
    private readonly Guid _orderId = Guid.NewGuid();

    public CheckoutBasketSagaCheckoutTests() =>
        _handler = new CheckoutBasketCommandHandler(_repository.Object);

    private BasketCheckoutDto SagaDto() => new()
    {
        OwnerId = OwnerId,
        CustomerId = Guid.NewGuid(),
        TotalPrice = 90m,
        OrderId = _orderId,
        ShippingAddress = new BasketCheckoutAddressDto
        {
            FirstName = "Ana", LastName = "Silva", EmailAddress = "ana@duckstore.dev",
            AddressLine = "Rua A, 1", Country = "BR", State = "SP", ZipCode = "01000-000"
        },
        Items = [new BasketCheckoutItemDto(Guid.NewGuid(), "Duck (Catalog name)", "img-1", 2, 45m)]
    };

    private void CloseReturns(CartCloseResult result) =>
        _repository
            .Setup(r => r.CloseCartAsync(
                OwnerId, CheckoutId, CartVersion, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task Handle_ShouldCloseTheCartWithTheSagasOrderIdAndLines_WithoutReadingIt()
    {
        CloseReturns(new CartCloseResult(CartCloseOutcome.Closed, _orderId));
        var dto = SagaDto();

        var result = await _handler.Handle(
            new CheckoutBasketCommand(dto, CheckoutId, CartVersion), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_orderId, result.OrderId);
        Assert.Null(result.ErrorType);
        _repository.Verify(r => r.CloseCartAsync(
                OwnerId, CheckoutId, CartVersion, _orderId,
                It.Is<string>(json => json.Contains("Duck (Catalog name)") && json.Contains(_orderId.ToString())),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(r => r.TryGetBasket(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldReturnTheStoredOrderId_OnAReplayOfTheSameAttempt()
    {
        var firstOrderId = Guid.NewGuid();
        CloseReturns(new CartCloseResult(CartCloseOutcome.Replayed, firstOrderId));

        var result = await _handler.Handle(
            new CheckoutBasketCommand(SagaDto(), CheckoutId, CartVersion), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(firstOrderId, result.OrderId);
    }

    [Fact]
    public async Task Handle_ShouldRefuseWithCartChanged_AsDataNotAnException()
    {
        CloseReturns(new CartCloseResult(CartCloseOutcome.CartChanged, Guid.Empty));

        var result = await _handler.Handle(
            new CheckoutBasketCommand(SagaDto(), CheckoutId, CartVersion), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.OrderId);
        Assert.Equal(CheckoutRefusals.CartChanged, result.ErrorType);
    }

    [Fact]
    public void Validator_ShouldRequireTheSagasOrderIdAndLines_WhenACheckoutIdIsGiven()
    {
        var dto = SagaDto();
        dto.OrderId = Guid.Empty;
        dto.Items = [];

        var failures = new CheckoutBasketCommandValidator()
            .Validate(new CheckoutBasketCommand(dto, CheckoutId, CartVersion)).ToList();

        Assert.Contains(failures, f => f.PropertyName == nameof(BasketCheckoutDto.OrderId));
        Assert.Contains(failures, f => f.PropertyName == nameof(BasketCheckoutDto.Items));
    }
}
