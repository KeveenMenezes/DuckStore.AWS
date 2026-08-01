namespace Challenges.Function.Modules.Questions.Data;

public interface IQuestionRepository
{
    // Writes both the PUBLIC and ANSWER items in one TransactWriteItems (ADR-0045 §2) — a
    // question is never visible half-written.
    Task AddAsync(Question question, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    // The only repository method that reads SK=ANSWER, used exclusively by the submit-answer and
    // reveal-hint Lambdas (ADR-0045 §2) — everything else reads through GSI1 or the AppSync direct
    // resolvers, neither of which this method is reachable from.
    Task<Question?> GetForGradingAsync(QuestionId id, CancellationToken cancellationToken = default);
}
