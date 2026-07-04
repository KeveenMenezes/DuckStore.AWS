using BuildingBlocks.Messaging.EventBridge;

namespace BuildingBlocks.Messaging.Streams;

// Runs every registered rule against one stream context and publishes the instruction of each
// rule whose Match returns true. This is the single place where stream rules meet the publisher,
// keeping the rules themselves free of any EventBridge knowledge.
public sealed class StreamRuleDispatcher<TImage>(
    IEnumerable<IStreamRule<TImage>> rules,
    IEventPublisher publisher)
{
    public async Task DispatchAsync(
        StreamContext<TImage> context, CancellationToken cancellationToken = default)
    {
        foreach (var rule in rules)
        {
            if (!rule.Match(context))
                continue;

            var instruction = await rule.BuildAsync(context, cancellationToken);
            await publisher.PublishAsync(instruction, cancellationToken);
        }
    }
}
