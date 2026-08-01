namespace Challenges.Function.Modules.Questions.Domain.Entities;

// The aggregate the whole ADR-0045 rewrite is named after: the answer key (AnswerKey) never
// leaves this type except through Grade/Explanation/Hint, which is exactly the surface the
// submit-answer and reveal-hint Lambdas need and nothing more.
public class Question : Aggregate<QuestionId>
{
    // Points lost per hint revealed before answering — a domain rule, not the SPA's concern
    // (moves HINT_PENALTY out of features/challenges/constants.ts, ADR-0045 §2).
    public const int HintPenalty = 25;

    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public Language Language { get; private set; } = default!;
    public Difficulty Difficulty { get; private set; }
    public int Points { get; private set; }

    private readonly List<string> _options = [];
    public IReadOnlyList<string> Options => _options.AsReadOnly();

    private AnswerKey _answerKey = default!;

    // Only DynamoQuestionRepository (same assembly) reads the raw answer key, to build the
    // ANSWER item — everyone else goes through Grade/Explanation/Hint.
    internal AnswerKey AnswerKey => _answerKey;

    public int HintCount => _answerKey.Hints.Count;

    public static Question Create(
        QuestionId id,
        string title,
        string description,
        string code,
        IEnumerable<string> options,
        Language language,
        Difficulty difficulty,
        int points,
        AnswerKey answerKey)
    {
        var optionList = options.ToList();
        if (optionList.Count == 0)
        {
            throw new BadRequestException(nameof(Options), optionList.Count, "must not be empty");
        }

        if (answerKey.CorrectAnswer >= optionList.Count)
        {
            throw new BadRequestException(
                nameof(AnswerKey.CorrectAnswer), answerKey.CorrectAnswer, "must index an existing option");
        }

        if (points <= 0)
        {
            throw new BadRequestException(nameof(Points), points, "must be greater than zero");
        }

        var question = new Question
        {
            Id = id,
            Title = title,
            Description = description,
            Code = code,
            Language = language,
            Difficulty = difficulty,
            Points = points,
            _answerKey = answerKey,
            CreatedAt = DateTime.UtcNow
        };

        question._options.AddRange(optionList);

        return question;
    }

    // Reconstitutes a persisted question from both the PUBLIC and ANSWER items — the only path
    // that ever holds a full answer key in memory outside of Create (ADR-0045 §2).
    public static Question Load(
        string id,
        string title,
        string description,
        string code,
        IEnumerable<string> options,
        string language,
        Difficulty difficulty,
        int points,
        AnswerKey answerKey)
    {
        var question = new Question
        {
            Id = QuestionId.Of(id),
            Title = title,
            Description = description,
            Code = code,
            Language = Language.Of(language),
            Difficulty = difficulty,
            Points = points,
            _answerKey = answerKey
        };

        question._options.AddRange(options);

        return question;
    }

    // The whole point of ADR-0045 §3: grading, and the point arithmetic it implies, is a domain
    // rule, never something a handler (or, worse, the client) computes.
    public AttemptResult Grade(int selectedOption, int hintsRevealed)
    {
        var isCorrect = selectedOption == _answerKey.CorrectAnswer;
        var earned = isCorrect ? Math.Max(0, Points - hintsRevealed * HintPenalty) : 0;

        return new AttemptResult(Id, isCorrect, selectedOption, hintsRevealed, earned);
    }

    public string Explanation => _answerKey.Explanation;

    // hintsRevealed is 1-based (how many hints have been unlocked so far); this returns the last
    // one unlocked. Bounds are enforced by the caller's DynamoDB condition (ADR-0045 §6), not here.
    public string Hint(int hintsRevealed) => _answerKey.Hints[hintsRevealed - 1];
}
