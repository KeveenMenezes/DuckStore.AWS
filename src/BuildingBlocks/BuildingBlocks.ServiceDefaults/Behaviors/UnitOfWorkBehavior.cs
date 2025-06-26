using BuildingBlocks.Core.Abstractions;

namespace BuildingBlocks.ServiceDefaults.Behaviors;

public class UnitOfWorkBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork
)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>
    where TResponse : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!IsCommand())
        {
            return await next();
        }

        var response = await next();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return response;
    }

    private static bool IsCommand()
    {
        return typeof(TRequest).Name.EndsWith("Command", StringComparison.OrdinalIgnoreCase);
    }
}
