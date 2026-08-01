namespace Challenges.Function.Modules.Questions.Domain.ValueObjects;

// Free-form (not a closed enum like Difficulty): the question bank spans an open set of
// languages, and this is GSI1PK (ADR-0045 §2), so normalizing case here is what keeps
// "Python"/"python" from silently splitting into two GSI1 partitions.
public class Language : ValueObject<string>
{
    private Language(string value) : base(value) { }

    public static Language Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BadRequestException(nameof(Language), value, "must not be empty");
        }

        return new Language(value.Trim().ToLowerInvariant());
    }
}
