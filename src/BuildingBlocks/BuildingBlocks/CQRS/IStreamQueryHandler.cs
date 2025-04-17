using MediatR;

namespace BuildingBlocks.CQRS;

public interface IStreamQueryHandler<in IQueryStream, TResponse>
    : IStreamRequestHandler<IQueryStream, TResponse>
    where IQueryStream : IQueryStream<TResponse>
    where TResponse : notnull
{
}
