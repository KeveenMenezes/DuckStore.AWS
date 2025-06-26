using System.Reflection;
using BuildingBlocks.Messaging.MassTransit;
using MassTransit;
using Ordering.Domain.AggregatesModel.OrderAggregate.Abstractions;

namespace Ordering.Infrastructure.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddMessageBroker(
                configuration,
                Assembly.GetExecutingAssembly(),
                additionalConfig: config =>
                {
                    config.AddEntityFrameworkOutbox<ApplicationDbContext>(o =>
                    {
                        o.UseSqlServer();
                        o.UseBusOutbox();
                    });
                });

        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseSqlServer(configuration.GetConnectionString("orderingDb"));
        });

        return services;
    }
}
