using BuildingBlocks.Core.Exceptions;
using BuildingBlocks.Messaging.Events;
using Review.Function.Modules.Reviews.EventsIntegration.Consumers.OrderCompleted;

namespace Review.UnitTests.EventsIntegration.Consumers;

public class OrderCompletedMapperTests
{
    [Fact]
    public void ToCommand_MapsCustomerAndProducts()
    {
        var customerId = Guid.NewGuid();
        List<Guid> productIds = [Guid.NewGuid(), Guid.NewGuid()];

        var command = OrderCompletedMapper.ToCommand(new OrderCompletedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = customerId,
            ProductIds = productIds
        });

        Assert.Equal(customerId, command.CustomerId);
        Assert.Equal(productIds, command.ProductIds);
    }

    [Fact]
    public void ToCommand_RejectsEmptyCustomerId()
    {
        var ex = Assert.Throws<ValidationException>(() => OrderCompletedMapper.ToCommand(new OrderCompletedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.Empty,
            ProductIds = [Guid.NewGuid()]
        }));

        Assert.Contains(ex.Failures, f => f.PropertyName == nameof(OrderCompletedEvent.CustomerId));
    }

    [Fact]
    public void ToCommand_RejectsEmptyProductList()
    {
        var ex = Assert.Throws<ValidationException>(() => OrderCompletedMapper.ToCommand(new OrderCompletedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProductIds = []
        }));

        Assert.Contains(ex.Failures, f => f.PropertyName == nameof(OrderCompletedEvent.ProductIds));
    }

    [Fact]
    public void ToCommand_RejectsEmptyGuidProductId()
    {
        var ex = Assert.Throws<ValidationException>(() => OrderCompletedMapper.ToCommand(new OrderCompletedEvent
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProductIds = [Guid.NewGuid(), Guid.Empty]
        }));

        Assert.Contains(ex.Failures, f => f.PropertyName == nameof(OrderCompletedEvent.ProductIds));
    }
}
