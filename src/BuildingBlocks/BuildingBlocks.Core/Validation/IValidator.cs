namespace BuildingBlocks.Core.Validation;

/// <summary>
/// Hand-written validation for a command or query.
/// </summary>
/// <remarks>
/// Deliberately not FluentValidation or DataAnnotations: both discover rules by reflecting over
/// types and attributes, which Native AOT cannot follow and neither library annotates for the
/// trimmer — an AOT build would succeed and then fail at invocation (ADR-0042 §7). An interface
/// with a plain method has no such failure mode: the compiler sees every rule.
/// </remarks>
public interface IValidator<in T>
{
    /// <summary>Returns one failure per broken rule; an empty sequence means valid.</summary>
    IEnumerable<ValidationFailure> Validate(T instance);
}

public readonly record struct ValidationFailure(string PropertyName, string ErrorMessage);
