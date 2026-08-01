namespace Challenges.Function.Modules.Progress.Domain.Entities;

// Score, KPIs and the attempt history for one player (ADR-0045 §5). The repository never
// load-modify-saves the whole aggregate: Apply is always run against a fresh CreateEmpty shell,
// so every field it produces IS the delta the repository writes with ADD/SET — the invariants
// live here, the persistence stays a one-way write (ADR-0045 §5, §9).
public class PlayerProgress : Aggregate<OwnerId>
{
    // Below this, a redemption's TransactWriteItems cost isn't worth what it converts to
    // (ADR-0046 §2 leaves the exact economics to Pricing; this only guards against 1-point
    // redemptions). Pricing's own minimum-redeemable-amount is a separate, unrelated constant.
    public const int MinimumRedeemablePoints = 100;

    public int Score { get; private set; }
    public int PointsSpent { get; private set; }
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

    private readonly List<Redemption> _redemptions = [];
    public IReadOnlyList<Redemption> Redemptions => _redemptions.AsReadOnly();

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

    // Run against a CreateEmpty shell, same delta-only shape as Apply (ADR-0046 §2): Score carries
    // the *negative* delta the repository ADDs (never the resulting balance — the balance itself is
    // never read here, only enforced by the database's own ConditionExpression), PointsSpent the
    // positive one. The repository never decides how much to debit; it only translates these two
    // fields and the minted Redemption into the transaction's Update/Put.
    public Redemption Redeem(int points)
    {
        var redemption = Redemption.Create(points, DateTime.UtcNow);
        _redemptions.Add(redemption);

        Score -= points;
        PointsSpent += points;

        return redemption;
    }

    public static PlayerProgress Load(
        string ownerId,
        int score,
        int pointsSpent,
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
            PointsSpent = pointsSpent,
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
