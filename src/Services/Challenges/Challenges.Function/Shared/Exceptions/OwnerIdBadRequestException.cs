namespace Challenges.Function.Shared.Exceptions;

public class OwnerIdBadRequestException(string? value)
    : BadRequestException(nameof(OwnerId), value!, "must be a \"USER#<sub>\" identity");
