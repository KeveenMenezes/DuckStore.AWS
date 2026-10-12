using Payment.Function;

namespace Payment.UnitTests.Application.Commands;

public class RegisterPaymentContractsTests
{
    [Fact]
    public void RegisterPaymentRequest_ShouldNeverPrintCardData()
    {
        // Lambda and MediatR logging print whole records; card data must never reach a log (ADR-0050 §7).
        var request = new RegisterPaymentRequest(
            Guid.NewGuid(), Guid.NewGuid(), 150m, PaymentMethod: 2, Installments: 3,
            CardName: "Ana Silva", CardNumber: "4111111111111111", Expiration: "12/2030", Cvv: "123");

        var printed = request.ToString();

        Assert.DoesNotContain("4111111111111111", printed);
        Assert.DoesNotContain("123,", printed);
        Assert.DoesNotContain("12/2030", printed);
        Assert.DoesNotContain("Ana Silva", printed);
        Assert.Contains("[redacted]", printed);
        Assert.Contains(request.OrderId.ToString(), printed);
    }
}
