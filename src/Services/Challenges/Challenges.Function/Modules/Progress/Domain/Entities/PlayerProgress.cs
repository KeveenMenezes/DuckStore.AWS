namespace Challenges.Function.Modules.Progress.Domain.Entities;

// Score, KPIs and the attempt history for one player (ADR-0045 §5). The repository never
// load-modify-saves the whole aggregate: Apply is always run against a fresh CreateEmpty shell,
// so every field it produces IS the delta the repository writes with ADD/SET — the invariants
// live here, the persistence stays a one-way write (ADR-0045 §5, §9).
public class PlayerProgress : Aggregate<OwnerId>
{
    public int Score { get; private set; }
    public int Completed { get; private set; }
    public int CorrectCount { get; private set; }
    public int WrongCount { get; private set; }
    public int HintsUsed { get; private set; }
    public int CurrentStreak { get; private set; }
    public DateTime? LastAnsweredAt { get; private set; }

    private readonly Dictionary<string, int> _byLanguage = [];
    public IReadOnlyDictionary<string, int> ByLanguage => _byLanguage;

    private readonly List<Attempt> _attempts = [];
    public IReadOnlyList<Attempt> Attempts => _attempts.AsReadOnly();

    // The ledger rows this delta must commit alongside its Score change (ADR-0048 §2). Like every
    // other collection here it is delta-only: Load never fills it, because the ledger is read by
    // AppSync's myPointsHistory and never rehydrated into the aggregate.
    private readonly List<PointsTransaction> _pointsTransactions = [];
    public IReadOnlyList<PointsTransaction> PointsTransactions => _pointsTransactions.AsReadOnly();

    public static PlayerProgress CreateEmpty(OwnerId ownerId) => new() { Id = ownerId };

    public Attempt Apply(AttemptResult result, string language)
    {
        var attempt = Attempt.From(result, DateTime.UtcNow);
        _attempts.Add(attempt);

        Completed++;
        HintsUsed += result.HintsRevealed;
        LastAnsweredAt = attempt.AnsweredAt;

        if (result.IsCorrect)
        {
            Score += result.PointsEarned;

            // Only when something was earned: Question.Grade floors the award at zero, and a
            // zero-point credit moves no balance, so there is nothing for the ledger to explain
            // (ChallengeCredit rejects it by design).
            if (result.PointsEarned > 0)
            {
                _pointsTransactions.Add(
                    PointsTransaction.ChallengeCredit(Id, result.QuestionId, result.PointsEarned, attempt.AnsweredAt));
            }

            CorrectCount++;
            CurrentStreak++;
            _byLanguage[language] = _byLanguage.GetValueOrDefault(language) + 1;
        }
        else
        {
            WrongCount++;
            CurrentStreak = 0;
        }

        return attempt;
    }

    public static PlayerProgress Load(
        string ownerId,
        int score,
        int completed,
        int correctCount,
        int wrongCount,
        int hintsUsed,
        int currentStreak,
        DateTime? lastAnsweredAt,
        IReadOnlyDictionary<string, int> byLanguage,
        IEnumerable<Attempt> attempts)
    {
        var progress = new PlayerProgress
        {
            Id = OwnerId.Of(ownerId),
            Score = score,
            Completed = completed,
            CorrectCount = correctCount,
            WrongCount = wrongCount,
            HintsUsed = hintsUsed,
            CurrentStreak = currentStreak,
            LastAnsweredAt = lastAnsweredAt
        };

        foreach (var (key, value) in byLanguage)
        {
            progress._byLanguage[key] = value;
        }

        progress._attempts.AddRange(attempts);

        return progress;
    }
}
