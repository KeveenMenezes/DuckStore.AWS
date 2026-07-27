using BuildingBlocks.Core.Validation;

namespace BuildingBlocks.Core.Exceptions;

// Thrown by ValidationBehavior when a command fails its validator. Carries every failure, not
// just the first, so a caller can correct one round trip instead of several.
public class ValidationException(IReadOnlyList<ValidationFailure> failures)
    : Exception($"Validation failed: {string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"))}")
{
    public IReadOnlyList<ValidationFailure> Failures { get; } = failures;
}
