namespace BuildingBlocks.CQRS;

public interface IQueryStream<out TResponse> : IStreamRequest<TResponse>
    where TResponse : notnull
{
}
