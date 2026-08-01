namespace Challenges.Function.Modules.Progress.Data;

public interface IPlayerProgressRepository
{
    // Null when the question hasn't been answered yet — either no ATTEMPT row exists at all, or
    // one exists only as a hint-tracking placeholder (RevealHintAsync upserts it before any
    // answer is submitted).
    Task<Attempt?> GetAttemptAsync(
        OwnerId ownerId, QuestionId questionId, CancellationToken cancellationToken = default);

    // How many hints have been revealed for this question so far, 0 if none — unlike
    // GetAttemptAsync this also reads an unanswered, hint-only placeholder row, since
    // submit-answer needs the count regardless of whether the question was ever answered
    // (ADR-0045 §3: hintsRevealed always comes from the stored attempt, never the client).
    Task<int> GetHintsRevealedAsync(
        OwnerId ownerId, QuestionId questionId, CancellationToken cancellationToken = default);

    // Finalizes the attempt and folds its delta into PROFILE's counters in one TransactWriteItems
    // (ADR-0045 §4). `delta` MUST be a PlayerProgress.CreateEmpty shell that has had exactly one
    // Apply call — every field it carries IS the delta to ADD/SET, which is what lets this method
    // stay a mechanical translation with no scoring/streak logic of its own (that all lives in
    // PlayerProgress.Apply, per thin-handlers-rich-domain). A replay of an already-scored question
    // is not surfaced as an error: the stored attempt is re-read and returned instead of a fresh
    // one being written twice.
    Task<Attempt> SaveAttemptAsync(
        OwnerId ownerId, PlayerProgress delta, CancellationToken cancellationToken = default);

    // A plain read of the running total, for the submit-answer response's newScore — never used
    // to decide what to write (that stays a pure delta, ADR-0045 §5).
    Task<int> GetScoreAsync(OwnerId ownerId, CancellationToken cancellationToken = default);

    // ADDs one to the ATTEMPT row's HintsRevealed, conditional on the question not being answered
    // yet and the reveal count still under hintCount (ADR-0045 §6). Returns the new count, or
    // throws HintNotAvailableException when the condition fails.
    Task<int> RevealHintAsync(
        OwnerId ownerId, QuestionId questionId, int hintCount, CancellationToken cancellationToken = default);

    // Debits PROFILE and writes the REDEMPTION# ledger row in one TransactWriteItems (ADR-0046 §2).
    // `delta` MUST be a PlayerProgress.CreateEmpty shell that has had exactly one Redeem call —
    // Score carries the negative delta to ADD, PointsSpent the positive one, mirroring
    // SaveAttemptAsync's mechanical-translation contract. Throws InsufficientPointsException when
    // the database's own Score >= points condition fails; that failure never debits anything.
    Task<Redemption> RedeemPointsAsync(
        OwnerId ownerId, PlayerProgress delta, CancellationToken cancellationToken = default);
}
