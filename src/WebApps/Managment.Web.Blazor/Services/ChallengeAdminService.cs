namespace Managment.Web.Blazor.Services;

/// <summary>
/// Typed wrapper over the challenge-points admin GraphQL operations. Operation shapes mirror
/// graphql/schema.graphql (the single schema source of truth). updateChallengePoints is
/// Admin-group only — enforced server-side by its resolver.
/// </summary>
public sealed class ChallengeAdminService(GraphQLClient gql)
{
    private const string GetChallengesQuery =
        """
        query GetChallenges($pageSize: Int, $nextToken: String) {
          challenges(pageSize: $pageSize, nextToken: $nextToken) {
            items {
              id
              title
              language
              difficulty
              points
            }
            nextToken
          }
        }
        """;

    private const string UpdateChallengePointsMutation =
        """
        mutation UpdateChallengePoints($id: ID!, $points: Int!) {
          updateChallengePoints(id: $id, points: $points) {
            id
            points
          }
        }
        """;

    public async Task<ChallengePage> GetChallengesAsync(int pageSize = 20, string? nextToken = null)
    {
        var data = await gql.SendAsync<ChallengesData>(GetChallengesQuery, new { pageSize, nextToken });
        return data.Challenges;
    }

    // Returns the points value the server stored, so the list shows what the store will show.
    public async Task<int> UpdatePointsAsync(string id, int points)
    {
        var data = await gql.SendAsync<UpdatePointsData>(UpdateChallengePointsMutation, new { id, points });
        return data.UpdateChallengePoints.Points;
    }

    private sealed record ChallengesData(ChallengePage Challenges);

    private sealed record UpdatePointsData(UpdatePointsResult UpdateChallengePoints);

    private sealed record UpdatePointsResult(string Id, int Points);
}
