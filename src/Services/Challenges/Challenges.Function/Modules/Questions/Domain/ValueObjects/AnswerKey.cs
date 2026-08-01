namespace Challenges.Function.Modules.Questions.Domain.ValueObjects;

// The gabarito (ADR-0045 §2): correct option index, the explanation shown only after grading, and
// the hint ladder. Deliberately the only place these three ever sit together in memory — Question
// exposes them one at a time (Grade/Explanation/Hint), never as this whole object.
public class AnswerKey : ValueObject
{
    public int CorrectAnswer { get; }
    public string Explanation { get; } = default!;

    private readonly List<string> _hints;
    public IReadOnlyList<string> Hints => _hints.AsReadOnly();

    private AnswerKey(int correctAnswer, string explanation, IEnumerable<string> hints)
    {
        CorrectAnswer = correctAnswer;
        Explanation = explanation;
        _hints = [.. hints];
    }

    public static AnswerKey Of(int correctAnswer, string explanation, IEnumerable<string> hints)
    {
        if (correctAnswer < 0)
        {
            throw new BadRequestException(nameof(CorrectAnswer), correctAnswer, "must not be negative");
        }

        if (string.IsNullOrWhiteSpace(explanation))
        {
            throw new BadRequestException(nameof(Explanation), explanation, "must not be empty");
        }

        return new AnswerKey(correctAnswer, explanation, hints);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CorrectAnswer;
        yield return Explanation;
        foreach (var hint in _hints)
        {
            yield return hint;
        }
    }
}
