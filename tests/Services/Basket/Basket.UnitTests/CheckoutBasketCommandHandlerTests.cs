using Basket.Function.Modules.ShoppingCarts.Data;
using Basket.Function.Modules.ShoppingCarts.Domain.Dtos;
using Basket.Function.Modules.ShoppingCarts.Features.CheckoutBasket;

namespace Basket.UnitTests;

// The checkout payload and its validation. How the cart is closed (replay, CartChanged) is covered by
// CheckoutBasketSagaCheckoutTests and Data/CloseCartTests.
public class CheckoutBasketCommandHandlerTests
{
    private const string CheckoutId = "7d0f5c3e-1a2b-4c3d-8e9f-0a1b2c3d4e5f";

    private readonly Mock<IShoppingCartRepository> _repository = new();
    private readonly CheckoutBasketCommandValidator _validator = new();

    private static BasketCheckoutDto ValidDto() => new()
    {
        OwnerId = "USER#testuser",
        CustomerId = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        TotalPrice = 189.99m,
        ShippingAddress = ValidShippingAddress(),
        Items = [new BasketCheckoutItemDto(Guid.NewGuid(), "Sample Product", null, 1, 189.99m)]
    };

    [Fact]
    public async Task Handle_ShouldCarryPointsToUseAndTotalPriceUnchanged_IntoTheCheckoutEvent()
    {
        var dto = ValidDto();
        dto.PointsToUse = 1000;
        string? checkoutJson = null;
        _repository
            .Setup(r => r.CloseCartAsync(
                dto.OwnerId, CheckoutId, "v1", dto.OrderId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, Guid, string, CancellationToken>((_, _, _, _, json, _) => checkoutJson = json)
            .ReturnsAsync(new CartCloseResult(CartCloseOutcome.Closed, dto.OrderId));

        await new CheckoutBasketCommandHandler(_repository.Object)
            .Handle(new CheckoutBasketCommand(dto, CheckoutId, "v1"), CancellationToken.None);

        var evt = System.Text.Json.JsonSerializer.Deserialize(
            checkoutJson!,
            BuildingBlocks.Messaging.Serialization.MessagingSerializerContext.Default.BasketCheckoutEvent);

        Assert.NotNull(evt);
        Assert.Equal(1000, evt!.PointsToUse);
        Assert.Equal(189.99m, evt.TotalPrice);
        Assert.Equal(dto.OrderId, evt.OrderId);
    }

    [Fact]
    public void Validator_ShouldNotHaveErrors_WhenCommandIsValid()
    {
        var result = _validator.Validate(new CheckoutBasketCommand(ValidDto(), CheckoutId, "v1")).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void Validator_ShouldHaveErrors_WhenCommandIsInvalid()
    {
        var result = _validator.Validate(new CheckoutBasketCommand(null!, CheckoutId)).ToList();

        Assert.Contains(result, f => f.PropertyName == "BasketCheckoutDto" && f.ErrorMessage == "BasketCheckoutDto can't be null");
    }

    // Only the checkout saga calls Basket, and it always sends the attempt's id (ADR-0050 §2/§3).
    [Fact]
    public void Validator_ShouldRequireACheckoutId()
    {
        var result = _validator.Validate(new CheckoutBasketCommand(ValidDto(), CheckoutId: null)).ToList();

        Assert.Contains(result, f => f.PropertyName == "CheckoutId");
    }

    // Ordering's Address.Of rejects a blank AddressLine/EmailAddress. That rejection lands in
    // ordering-dlq long after the cart is gone, so the validator has to catch it here instead.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validator_ShouldHaveError_WhenAddressLineIsBlank(string? addressLine)
    {
        var dto = ValidDto();
        dto.ShippingAddress.AddressLine = addressLine!;

        var result = _validator.Validate(new CheckoutBasketCommand(dto, CheckoutId)).ToList();

        Assert.Contains(result, f => f.PropertyName == "AddressLine" && f.ErrorMessage == "AddressLine is required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validator_ShouldHaveError_WhenEmailAddressIsBlank(string? emailAddress)
    {
        var dto = ValidDto();
        dto.ShippingAddress.EmailAddress = emailAddress!;

        var result = _validator.Validate(new CheckoutBasketCommand(dto, CheckoutId)).ToList();

        Assert.Contains(result, f => f.PropertyName == "EmailAddress" && f.ErrorMessage == "EmailAddress is required");
    }

    private static BasketCheckoutAddressDto ValidShippingAddress() => new()
    {
        FirstName = "Test",
        LastName = "User",
        EmailAddress = "test@duckstore.dev",
        AddressLine = "221B Baker Street",
        Country = "US",
        State = "London",
        ZipCode = "00000"
    };
}
