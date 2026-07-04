using BuildingBlocks.Messaging.EventBridge;

namespace BuildingBlocks.Messaging.Streams;

// One business reason to publish, isolated from AWS. A rule answers "should this change be
// published?" (Match) and, when it should, builds a transport-agnostic PublishInstruction
// (BuildAsync). Rules hold only domain logic — they never touch IAmazonEventBridge or
// PutEventsRequest, and never instantiate publishing infrastructure.
public interface IStreamRule<TImage>
{
    bool Match(StreamContext<TImage> context);

    Task<PublishInstruction> BuildAsync(
        StreamContext<TImage> context, CancellationToken cancellationToken = default);
}
