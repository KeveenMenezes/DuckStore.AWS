namespace Challenges.Function.Modules.Progress.Domain.Entities;

// One row per (player, question) — Id is the QuestionId, unique within its owning
// PlayerProgress's partition (ADR-0045 §4).
public class Attempt : Entity<QuestionId>
{
    public bool IsCorrect { get; private set; }
    public int SelectedOption { get; private set; }
    public int HintsRevealed { get; private set; }
    public int PointsEarned { get; private set; }
    public DateTime AnsweredAt { get; private set; }

    private Attempt() { }

    internal static Attempt From(AttemptResult result, DateTime answeredAt) =>
        new()
        {
            Id = result.QuestionId,
            IsCorrect = result.IsCorrect,
            SelectedOption = result.SelectedOption,
            HintsRevealed = result.HintsRevealed,
            PointsEarned = result.PointsEarned,
            AnsweredAt = answeredAt
        };

    public static Attempt Load(
        string questionId,
        bool isCorrect,
        int selectedOption,
        int hintsRevealed,
        int pointsEarned,
        DateTime answeredAt) =>
        new()
        {
            Id = QuestionId.Of(questionId),
            IsCorrect = isCorrect,
            SelectedOption = selectedOption,
            HintsRevealed = hintsRevealed,
            PointsEarned = pointsEarned,
            AnsweredAt = answeredAt
        };
}
