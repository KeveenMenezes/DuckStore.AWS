using Pricing.Function.Modules.Campaigns.Features.EndCampaign;

namespace Pricing.UnitTests.Application.Commands;

public class EndCampaignTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<ICampaignRepository> _campaignRepository;
    private readonly EndCampaignCommandValidator _validator;
    private readonly EndCampaignHandler _handler;

    public EndCampaignTests()
    {
        _autoMocker = new AutoMocker();
        _campaignRepository = _autoMocker.GetMock<ICampaignRepository>();
        _validator = new EndCampaignCommandValidator();
        _handler = _autoMocker.CreateInstance<EndCampaignHandler>();
    }

    [Fact]
    public async Task Handle_ShouldCancelCampaign_AndRetractItsProductDiscounts()
    {
        var campaign = Campaign.Create(
            CampaignId.Of(Guid.NewGuid()),
            "Black Friday",
            DiscountValue.Of(DiscountType.Fixed, 10),
            new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            [ProductId.Of(Guid.NewGuid())]);

        _campaignRepository
            .Setup(repo => repo.GetByIdAsync(campaign.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(campaign);

        var command = new EndCampaignCommand(campaign.Id.Value);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.True(result.Ended);
        Assert.True(campaign.IsCancelled);
        _campaignRepository.Verify(
            repo => repo.CancelAsync(campaign, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenCampaignDoesNotExist()
    {
        _campaignRepository
            .Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Campaign?)null);

        var command = new EndCampaignCommand(Guid.NewGuid());

        await Assert.ThrowsAsync<CampaignIdBadRequestException>(
            () => _handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenCampaignIdIsEmpty()
    {
        var result = _validator.TestValidate(new EndCampaignCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(x => x.CampaignId);
    }
}
