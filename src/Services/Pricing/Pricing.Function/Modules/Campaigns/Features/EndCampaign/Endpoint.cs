using Mapster;
using Pricing.Function.Modules.Campaigns.Features.EndCampaign;

namespace Pricing.Function;

public record EndCampaignRequest(Guid CampaignId);
public record EndCampaignResponse(Guid CampaignId, bool Ended);

// AppSync Mutation resolver (Lambda-backed per ADR-0009 — reads the campaign to know which
// product-discounts rows to retract, then fans out a TransactWriteItems delete).
public partial class Functions
{
    [LambdaFunction(PackageType = LambdaPackageType.Image)]
    public async Task<EndCampaignResponse> EndCampaign(
        EndCampaignRequest request,
        [FromServices] ISender sender)
    {
        var command = request.Adapt<EndCampaignCommand>();
        var result = await sender.Send(command, CancellationToken.None);
        return result.Adapt<EndCampaignResponse>();
    }
}
