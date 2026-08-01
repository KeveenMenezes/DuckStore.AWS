namespace Challenges.Function.Shared.Exceptions;

public class QuestionNotFoundException(string questionId)
    : NotFoundException(nameof(QuestionId), questionId);
