using BuildingBlocks.Core.Exceptions;
using BuildingBlocks.Core.Validation;

namespace BuildingBlocks.ServiceDefaults.Lambda.Behaviors;

public class ValidationBehavior<TRequest, TResponse>
    (IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMessage
{
    public ValueTask<TResponse> Handle(
        TRequest message,
        MessageHandlerDelegate<TRequest, TResponse> next,
        CancellationToken cancellationToken)
    {
        // Validators are synchronous by design — a rule that needs I/O is a domain invariant
        // and belongs in the handler or the entity, not in the pipeline.
        var failures = validators.SelectMany(v => v.Validate(message)).ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return next(message, cancellationToken);
    }
}
