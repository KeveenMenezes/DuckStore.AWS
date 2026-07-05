using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Streams;

namespace BuildingBlocks.UnitTests.Streams;

public class StreamRuleDispatcherTests
{
    public sealed record AnyImage(string Value);

    private static StreamContext<AnyImage> AnyContext() =>
        new("INSERT", null, new AnyImage("new"));

    [Fact]
    public async Task DispatchAsync_PublishesInstruction_WhenSingleRuleMatches()
    {
        var publisher = new Mock<IEventPublisher>();
        var rule = RuleReturning(match: true, new PublishInstruction("matched", new object()));

        var dispatcher = new StreamRuleDispatcher<AnyImage>([rule.Object], publisher.Object);

        await dispatcher.DispatchAsync(AnyContext());

        publisher.Verify(
            p => p.PublishAsync(
                It.Is<PublishInstruction>(i => i.DetailType == "matched"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_PublishesNothing_WhenNoRuleMatches()
    {
        var publisher = new Mock<IEventPublisher>();
        var rule = RuleReturning(match: false, new PublishInstruction("skipped", new object()));

        var dispatcher = new StreamRuleDispatcher<AnyImage>([rule.Object], publisher.Object);

        await dispatcher.DispatchAsync(AnyContext());

        publisher.Verify(
            p => p.PublishAsync(It.IsAny<PublishInstruction>(), It.IsAny<CancellationToken>()),
            Times.Never);
        rule.Verify(
            r => r.BuildAsync(It.IsAny<StreamContext<AnyImage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_PublishesOnlyInstructionsFromMatchingRules_WhenMultipleRulesRegistered()
    {
        var publisher = new Mock<IEventPublisher>();
        var matching = RuleReturning(match: true, new PublishInstruction("matched", new object()));
        var notMatching = RuleReturning(match: false, new PublishInstruction("skipped", new object()));

        var dispatcher = new StreamRuleDispatcher<AnyImage>(
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
    }

    [Fact]
    public async Task DispatchAsync_PropagatesException_WhenRuleBuildThrows()
    {
        var publisher = new Mock<IEventPublisher>();
        var rule = new Mock<IStreamRule<AnyImage>>();
        rule.Setup(r => r.Match(It.IsAny<StreamContext<AnyImage>>())).Returns(true);
        rule.Setup(r => r.BuildAsync(It.IsAny<StreamContext<AnyImage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rule failed"));

        var dispatcher = new StreamRuleDispatcher<AnyImage>([rule.Object], publisher.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(AnyContext()));

        publisher.Verify(
            p => p.PublishAsync(It.IsAny<PublishInstruction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IStreamRule<AnyImage>> RuleReturning(bool match, PublishInstruction instruction)
    {
        var rule = new Mock<IStreamRule<AnyImage>>();
        rule.Setup(r => r.Match(It.IsAny<StreamContext<AnyImage>>())).Returns(match);
        rule.Setup(r => r.BuildAsync(It.IsAny<StreamContext<AnyImage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instruction);
        return rule;
    }
}
