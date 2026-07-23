namespace PaymentGateway.Function.Shared.Configuration;

// Single DI composition for the whole service. No MediatR/FluentValidation pipeline — there is
// exactly one operation (simulate a gateway decision), so the CQRS ceremony isn't warranted
// (mirrors Review.Function's leanness for single-purpose Lambdas).
public static class ServiceRegistration
{
    public static IServiceCollection AddPaymentGatewayServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.AddEventBridgeMessaging(configuration);

        return services;
    }
}
