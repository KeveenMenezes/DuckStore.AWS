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

    // A fan-out rule turns one record into several events; they join the same single publish call.
    [Fact]
    public async Task DispatchAsync_PublishesEveryInstructionOfAMatchingFanOutRule_AlongsideTheRules()
    {
        var publisher = new Mock<IEventPublisher>();
        var rule = RuleReturning(match: true, new PublishInstruction("single", new object()));
        var fanOut = FanOutRuleReturning(match: true,
            new PublishInstruction("many", new object()), new PublishInstruction("many", new object()));
        var skipped = FanOutRuleReturning(match: false, new PublishInstruction("skipped", new object()));

        var dispatcher = new StreamRuleDispatcher<AnyImage>([rule.Object], publisher.Object, [fanOut.Object, skipped.Object]);

        await dispatcher.DispatchAsync([AnyContext("a"), AnyContext("b")]);

        publisher.Verify(
            p => p.PublishManyAsync(
                It.Is<IReadOnlyList<PublishInstruction>>(instructions =>
                    instructions.Count(i => i.DetailType == "single") == 2
                    && instructions.Count(i => i.DetailType == "many") == 4
                    && instructions.All(i => i.DetailType != "skipped")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        skipped.Verify(
            r => r.BuildAllAsync(It.IsAny<StreamContext<AnyImage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_PublishesNothing_WhenAFanOutRuleMatchesButAffectsNothing()
    {
        var publisher = new Mock<IEventPublisher>();
        var fanOut = FanOutRuleReturning(match: true);

        var dispatcher = new StreamRuleDispatcher<AnyImage>([], publisher.Object, [fanOut.Object]);

        await dispatcher.DispatchAsync(AnyContext());

        publisher.Verify(
            p => p.PublishManyAsync(It.IsAny<IReadOnlyList<PublishInstruction>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IStreamFanOutRule<AnyImage>> FanOutRuleReturning(bool match, params PublishInstruction[] instructions)
    {
        var rule = new Mock<IStreamFanOutRule<AnyImage>>();
        rule.Setup(r => r.Match(It.IsAny<StreamContext<AnyImage>>())).Returns(match);
        rule.Setup(r => r.BuildAllAsync(It.IsAny<StreamContext<AnyImage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instructions);
        return rule;
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
