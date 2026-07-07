using Pricing.Function.Modules.Campaigns.Features.CreateCampaign;

namespace Pricing.UnitTests.Application.Commands;

public class CreateCampaignTests
{
    private readonly AutoMocker _autoMocker;
    private readonly Mock<ICampaignRepository> _campaignRepository;
    private readonly CreateCampaignCommandValidator _validator;
    private readonly CreateCampaignHandler _handler;

    public CreateCampaignTests()
    {
        _autoMocker = new AutoMocker();
        _campaignRepository = _autoMocker.GetMock<ICampaignRepository>();
        _validator = new CreateCampaignCommandValidator();
        _handler = _autoMocker.CreateInstance<CreateCampaignHandler>();
    }

    private static CreateCampaignCommand ValidCommand() => new(
        "Black Friday",
        DiscountType.Percentage,
        20,
        new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
        [Guid.NewGuid(), Guid.NewGuid()]);

    [Fact]
    public async Task Handle_ShouldPersistCampaign_WithAllEnrolledProducts()
    {
        var command = ValidCommand();

        Campaign? saved = null;
        _campaignRepository
            .Setup(repo => repo.AddAsync(It.IsAny<Campaign>(), It.IsAny<CancellationToken>()))
            .Callback<Campaign, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.NotNull(saved);
        Assert.Equal(command.ProductIds.Count, saved!.ProductIds.Count);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenEndsAtIsNotAfterStartsAt()
    {
        var command = ValidCommand() with { EndsAt = ValidCommand().StartsAt };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.EndsAt);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenProductIdsIsEmpty()
    {
        var command = ValidCommand() with { ProductIds = [] };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProductIds);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenValueIsNotPositive()
    {
        var command = ValidCommand() with { Value = 0 };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Value);
    }
}
