using Pricing.Function.Modules.Campaigns.Features.CreateCampaign;

namespace Pricing.Function;

// DiscountType travels as a string over the AppSync/Lambda JSON boundary (GraphQL enum-as-string,
// like the rest of this API) and is parsed here rather than relying on a JSON enum converter.
public record CreateCampaignRequest(
    string Name, string DiscountType, decimal Value, DateTime StartsAt, DateTime EndsAt, List<Guid> ProductIds);
public record CreateCampaignResponse(Guid Id);

// AppSync Mutation resolver (Lambda-backed per ADR-0009 — fans out a TransactWriteItems across
// "campaigns" and "product-discounts", plus FluentValidation of the campaign period/products).
public partial class Functions
{
    [LambdaFunction]
    public async Task<CreateCampaignResponse> CreateCampaign(
        CreateCampaignRequest request,
        [FromServices] ISender sender)
    {
        var command = new CreateCampaignCommand(
            request.Name,
            Enum.Parse<Modules.Campaigns.Domain.Enums.DiscountType>(request.DiscountType, ignoreCase: true),
            request.Value,
            request.StartsAt,
            request.EndsAt,
            request.ProductIds);

        var result = await sender.Send(command, CancellationToken.None);
        return new CreateCampaignResponse(result.Id);
    }
}
