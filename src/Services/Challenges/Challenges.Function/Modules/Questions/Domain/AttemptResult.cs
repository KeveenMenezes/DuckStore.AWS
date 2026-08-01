namespace Challenges.Function.Modules.Questions.Domain;

// The outcome of Question.Grade — folded into PlayerProgress.Apply by the submit-answer handler
// (ADR-0045 §3). PointsEarned is already floored at zero here; nothing downstream re-derives it.
public sealed record AttemptResult(
    QuestionId QuestionId,
    bool IsCorrect,
    int SelectedOption,
    int HintsRevealed,
    int PointsEarned);
