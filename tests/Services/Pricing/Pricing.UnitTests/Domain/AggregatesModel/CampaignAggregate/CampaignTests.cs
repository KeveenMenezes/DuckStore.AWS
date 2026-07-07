namespace Pricing.UnitTests.Domain.AggregatesModel.CampaignAggregate;

public class CampaignTests
{
    private static readonly DateTime StartsAt = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndsAt = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);

    private static IEnumerable<ProductId> OneProduct() => [ProductId.Of(Guid.NewGuid())];

    [Fact]
    public void Create_ShouldSucceed_WithValidPeriodAndProducts()
    {
        var campaign = Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            "Black Friday",
            DiscountValue.Of(DiscountType.Percentage, 20),
            StartsAt,
            EndsAt,
            OneProduct());

        Assert.Equal("Black Friday", campaign.Name);
        Assert.Single(campaign.ProductIds);
        Assert.False(campaign.IsCancelled);
    }

    [Fact]
    public void Create_ShouldThrow_WhenEndsAtIsNotAfterStartsAt()
    {
        Assert.Throws<CampaignPeriodBadRequestException>(() => Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            "Black Friday",
            DiscountValue.Of(DiscountType.Percentage, 20),
            StartsAt,
            StartsAt,
            OneProduct()));
    }

    [Fact]
    public void Create_ShouldThrow_WhenProductIdsIsEmpty()
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.BadRequestException>(() => Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            "Black Friday",
            DiscountValue.Of(DiscountType.Percentage, 20),
            StartsAt,
            EndsAt,
            []));
    }

    [Fact]
    public void Cancel_ShouldMarkCampaignAsCancelled()
    {
        var campaign = Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            "Black Friday",
            DiscountValue.Of(DiscountType.Fixed, 10),
            StartsAt,
            EndsAt,
            OneProduct());

        campaign.Cancel();

        Assert.True(campaign.IsCancelled);
    }
}
