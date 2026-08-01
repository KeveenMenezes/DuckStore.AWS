namespace Challenges.Function.Shared.Exceptions;

public class QuestionIdBadRequestException(string? value)
    : BadRequestException(nameof(QuestionId), value!, "must not be empty");
