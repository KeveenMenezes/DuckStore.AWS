using BuildingBlocks.Messaging.EventBridge;
using BuildingBlocks.Messaging.Streams;

namespace BuildingBlocks.UnitTests.Streams;

public class StreamRuleDispatcherTests
{
    public sealed record AnyImage(string Value);

    private static StreamContext<AnyImage> AnyContext(string value = "new") =>
        new("INSERT", null, new AnyImage(value));

    [Fact]
    public async Task DispatchAsync_PublishesInstruction_WhenSingleRuleMatches()
    {
        var publisher = new Mock<IEventPublisher>();
        var rule = RuleReturning(match: true, new PublishInstruction("matched", new object()));

        var dispatcher = new StreamRuleDispatcher<AnyImage>([rule.Object], publisher.Object);

        await dispatcher.DispatchAsync(AnyContext());

        publisher.Verify(
            p => p.PublishManyAsync(
                It.Is<IReadOnlyList<PublishInstruction>>(
                    instructions => instructions.Count == 1 && instructions[0].DetailType == "matched"),
                It.IsAny<CancellationToken>()),
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
            p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()),
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
            p => p.PublishManyAsync(
                It.Is<IReadOnlyList<PublishInstruction>>(
                    instructions => instructions.Count == 1 && instructions[0].DetailType == "matched"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // The point of dispatching the whole batch rather than a record at a time: a Streams invocation
    // carrying several records costs one publish call, not one per record.
    [Fact]
    public async Task DispatchAsync_PublishesEveryMatchedInstructionInOneCall_WhenGivenAWholeBatch()
    {
        var publisher = new Mock<IEventPublisher>();
        var rule = RuleReturning(match: true, new PublishInstruction("matched", new object()));

        var dispatcher = new StreamRuleDispatcher<AnyImage>([rule.Object], publisher.Object);

        await dispatcher.DispatchAsync([AnyContext("a"), AnyContext("b"), AnyContext("c")]);

        publisher.Verify(
            p => p.PublishManyAsync(
                It.Is<IReadOnlyList<PublishInstruction>>(instructions => instructions.Count == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
            p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()),
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
