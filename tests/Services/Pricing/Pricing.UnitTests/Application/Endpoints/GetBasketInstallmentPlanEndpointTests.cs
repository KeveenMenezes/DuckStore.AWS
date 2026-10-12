using BuildingBlocks.Core.Exceptions;
using BuildingBlocks.Core.Validation;
using Mediator;
using Pricing.Function;
using Pricing.Function.Modules.Prices.Features.GetBasketInstallmentPlan;
using Pricing.Function.Shared.Exceptions;

namespace Pricing.UnitTests.Application.Endpoints;

// The checkout saga treats a thrown Lambda error as an ambiguous fault (InternalError + alarm), so
// anything a client can cause must come back as a typed Rejection instead (ADR-0050 §8).
public class GetBasketInstallmentPlanEndpointTests
{
    private readonly Mock<ISender> _sender = new();

    private static GetBasketInstallmentPlanRequest Request() =>
        new([new BasketInstallmentItemRequest(Guid.NewGuid(), 1)], ExpectedTotal: 10m, PaymentMethod: 2, Installments: 1);

    private void QueryThrows(Exception exception) =>
        _sender
            .Setup(s => s.Send(It.IsAny<GetBasketInstallmentPlanQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

    [Fact]
    public async Task ShouldRejectAsBadRequest_WhenTheQueryFailsValidation()
    {
        QueryThrows(new ValidationException([new ValidationFailure("Items[0].Quantity", "Quantity must be greater than zero")]));

        var response = await new Functions().GetBasketInstallmentPlan(Request(), _sender.Object);

        Assert.Equal("BadRequest", response.Rejection?.ErrorType);
    }

    [Fact]
    public async Task ShouldRejectAsBadRequest_WhenTheInstallmentCountIsNotOffered()
    {
        QueryThrows(new BadRequestException("installments", 7, "is not an available installment option"));

        var response = await new Functions().GetBasketInstallmentPlan(Request(), _sender.Object);

        Assert.Equal("BadRequest", response.Rejection?.ErrorType);
    }

    [Fact]
    public async Task ShouldRejectAsProductUnavailable_WhenAProductHasNoPrice()
    {
        QueryThrows(new PriceNotFoundException(Guid.NewGuid()));

        var response = await new Functions().GetBasketInstallmentPlan(Request(), _sender.Object);

        Assert.Equal("ProductUnavailable", response.Rejection?.ErrorType);
    }
}
