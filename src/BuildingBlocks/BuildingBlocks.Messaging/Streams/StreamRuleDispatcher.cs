using BuildingBlocks.Messaging.EventBridge;

namespace BuildingBlocks.Messaging.Streams;

// Runs every registered rule against one stream context and publishes the instruction of each
// rule whose Match returns true. This is the single place where stream rules meet the publisher,
// keeping the rules themselves free of any EventBridge knowledge.
public sealed class StreamRuleDispatcher<TImage>(
    IEnumerable<IStreamRule<TImage>> rules,
    IEventPublisher publisher)
{
    public Task DispatchAsync(
        StreamContext<TImage> context, CancellationToken cancellationToken = default) =>
        DispatchAsync([context], cancellationToken);

    // Takes the whole Streams batch, not one record at a time: every matched instruction across
    // every record is collected first and published in a single batched call, so a batchSize=10
    // invocation makes one PutEvents call instead of up to ten. Rules still run in record order,
    // and a rule that throws still aborts before anything is published.
    public async Task DispatchAsync(
        IReadOnlyList<StreamContext<TImage>> contexts, CancellationToken cancellationToken = default)
    {
        var instructions = new List<PublishInstruction>();

        foreach (var context in contexts)
        {
            foreach (var rule in rules)
            {
                if (!rule.Match(context))
                    continue;

                instructions.Add(await rule.BuildAsync(context, cancellationToken));
            }
        }

        if (instructions.Count == 0)
            return;

        await publisher.PublishManyAsync(instructions, cancellationToken);
    }
}
