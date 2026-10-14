using BuildingBlocks.Messaging.EventBridge;

namespace BuildingBlocks.Messaging.Streams;

// An IStreamRule for a change that publishes one event per affected entity, not one per record:
// e.g. deactivating a warehouse changes the storefront total of every product it holds. Same contract
// otherwise — domain logic only, no EventBridge types — and an empty list publishes nothing.
public interface IStreamFanOutRule<TImage>
{
    bool Match(StreamContext<TImage> context);

    Task<IReadOnlyList<PublishInstruction>> BuildAllAsync(
        StreamContext<TImage> context, CancellationToken cancellationToken = default);
}
