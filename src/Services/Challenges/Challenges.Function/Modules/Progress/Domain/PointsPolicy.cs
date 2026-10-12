namespace Challenges.Function.Modules.Progress.Domain;

// Point amounts that are product rules, not configuration: changing one is a business decision
// that ships by PR (SPEC-review-points). Challenge credits are per question and editable by
// admins (SPEC-challenge-points-admin), so they don't live here.
public static class PointsPolicy
{
    // Business rule: 15 points the first time a buyer publishes a review of a product, once per
    // product (SPEC.md).
    public const int ReviewCredit = 15;
}
