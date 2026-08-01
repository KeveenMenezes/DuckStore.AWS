namespace Challenges.Function.Modules.Questions.Domain.ValueObjects;

// A short human-assigned slug ("py-001", "js-002"), not a Guid — the question bank is seeded
// from src/WebApps/Shopping.Web.SPA.React/features/challenges/data/challenges.data.ts, whose ids
// are already this shape (ADR-0045 §10).
public class QuestionId : ValueObject<string>
{
    private QuestionId(string value) : base(value) { }

    public static QuestionId Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new QuestionIdBadRequestException(value);
        }

        return new QuestionId(value);
    }
}
