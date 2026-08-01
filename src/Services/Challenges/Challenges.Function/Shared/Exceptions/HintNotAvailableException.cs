namespace Challenges.Function.Shared.Exceptions;

// Thrown when a hint is requested after the question was already answered, or once every hint
// has already been revealed (ADR-0045 §6) — either way, no penalty is charged.
public class HintNotAvailableException(string questionId)
    : DomainException(nameof(QuestionId), questionId, "no further hint is available (already answered, or every hint was already revealed)");
