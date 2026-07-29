namespace Managment.Web.Blazor.Services;

/// <summary>
/// Typed wrapper over the discount-campaign GraphQL operations. Operation shapes mirror
/// graphql/schema.graphql (the single schema source of truth). Admin-group only —
/// enforced server-side by every resolver (createCampaign/endCampaign/campaigns).
/// </summary>
public sealed class CampaignAdminService(GraphQLClient gql)
{
    private const string GetCampaignsQuery =
        """
        query GetCampaigns($pageSize: Int, $nextToken: String) {
          campaigns(pageSize: $pageSize, nextToken: $nextToken) {
            items {
              id
              name
              discountType
              value
              startsAt
              endsAt
              productIds
              status
            }
            nextToken
          }
        }
        """;

    private const string CreateCampaignMutation =
        """
        mutation CreateCampaign(
          $name: String!
          $discountType: String!
          $value: Float!
          $startsAt: String!
          $endsAt: String!
          $productIds: [ID!]!
        ) {
          createCampaign(
            name: $name
            discountType: $discountType
            value: $value
            startsAt: $startsAt
            endsAt: $endsAt
            productIds: $productIds
          ) {
            id
          }
        }
        """;

    private const string EndCampaignMutation =
        """
        mutation EndCampaign($campaignId: ID!) {
          endCampaign(campaignId: $campaignId)
        }
        """;

    public async Task<CampaignPage> GetCampaignsAsync(int pageSize = 10, string? nextToken = null)
    {
        var data = await gql.SendAsync<CampaignsData>(GetCampaignsQuery, new { pageSize, nextToken });
        return data.Campaigns;
    }

    // StartsAt/EndsAt cross the wire as ISO 8601 strings — the same format
    // DynamoCampaignRepository writes with .ToString("o").
    public async Task<string> CreateCampaignAsync(CampaignFormModel model)
    {
        var data = await gql.SendAsync<CreateCampaignData>(
            CreateCampaignMutation,
            new
            {
                name = model.Name,
                discountType = model.DiscountType,
                value = model.Value,
                startsAt = model.StartsAt.ToUniversalTime().ToString("o"),
                endsAt = model.EndsAt.ToUniversalTime().ToString("o"),
                productIds = model.ProductIds,
            });
        return data.CreateCampaign.Id;
    }

    public async Task<bool> EndCampaignAsync(string campaignId)
    {
        var data = await gql.SendAsync<EndCampaignData>(EndCampaignMutation, new { campaignId });
        return data.EndCampaign;
    }

    private sealed record CampaignsData(CampaignPage Campaigns);

    private sealed record CreateCampaignData(CreateCampaignResult CreateCampaign);

    private sealed record CreateCampaignResult(string Id);

    private sealed record EndCampaignData(bool EndCampaign);
}
