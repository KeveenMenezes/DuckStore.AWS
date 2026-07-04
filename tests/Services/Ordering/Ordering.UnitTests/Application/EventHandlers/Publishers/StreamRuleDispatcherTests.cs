using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Streams;
using Ordering.Function.Modules.Orders.EventsIntegration.Publishers;

namespace Ordering.UnitTests.Application.EventHandlers.Publishers;

public class StreamRuleDispatcherTests
{
    private static StreamContext<OrderStreamImage> AnyContext() =>
        new("INSERT", null, new OrderStreamImage(Guid.NewGuid(), "Order", "Pending"));

    [Fact]
    public async Task DispatchAsync_PublishesOnlyInstructionsFromMatchingRules()
    {
        var publisher = new Mock<IEventPublisher>();
        var matching = RuleReturning(match: true, new PublishInstruction("matched", new object()));
        var notMatching = RuleReturning(match: false, new PublishInstruction("skipped", new object()));

        var dispatcher = new StreamRuleDispatcher<OrderStreamImage>(
            [matching.Object, notMatching.Object], publisher.Object);

        await dispatcher.DispatchAsync(AnyContext());

        publisher.Verify(
            p => p.PublishAsync(
                It.Is<PublishInstruction>(i => i.DetailType == "matched"), It.IsAny<CancellationToken>()),
            Times.Once);
        publisher.Verify(
            p => p.PublishAsync(
                It.Is<PublishInstruction>(i => i.DetailType == "skipped"), It.IsAny<CancellationToken>()),
            Times.Never);

        // A rule that does not Match is never asked to build an instruction.
        notMatching.Verify(
            r => r.BuildAsync(It.IsAny<StreamContext<OrderStreamImage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IStreamRule<OrderStreamImage>> RuleReturning(bool match, PublishInstruction instruction)
    {
        var rule = new Mock<IStreamRule<OrderStreamImage>>();
        rule.Setup(r => r.Match(It.IsAny<StreamContext<OrderStreamImage>>())).Returns(match);
        rule.Setup(r => r.BuildAsync(It.IsAny<StreamContext<OrderStreamImage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instruction);
        return rule;
    }
}
