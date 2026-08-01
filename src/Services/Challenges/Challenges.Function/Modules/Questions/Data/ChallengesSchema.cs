namespace Challenges.Function.Modules.Questions.Data;

// Single source of truth for the "challenges" table shape, shared by the repository and the
// development seeder. Every question is two items under the same QuestionId partition key
// (ADR-0045 §2): PUBLIC carries GSI1PK/GSI1SK, ANSWER deliberately does not — a sparse GSI1 that
// makes leaking the answer key through the public browse/search path a schema change, not a
// review-time slip.
public static class ChallengesSchema
{
    public const string TableName = "challenges";
    public const string Gsi1Name = "GSI1";

    public const string PublicSortKey = "PUBLIC";
    public const string AnswerSortKey = "ANSWER";

    public static string ComposeGsi1Sk(Difficulty difficulty, string questionId) =>
        $"{difficulty}#{questionId}";
}
