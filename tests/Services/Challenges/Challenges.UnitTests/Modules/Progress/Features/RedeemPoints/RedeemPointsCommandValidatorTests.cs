using Challenges.Function.Modules.Progress.Features.RedeemPoints;

namespace Challenges.UnitTests.Modules.Progress.Features.RedeemPoints;

public class RedeemPointsCommandValidatorTests
{
    private readonly RedeemPointsCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldHaveNoErrors_ForAValidCommand()
    {
        var command = new RedeemPointsCommand("USER#alice", PlayerProgress.MinimumRedeemablePoints);

        Assert.Empty(_validator.Validate(command));
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenOwnerIdIsMissing()
    {
        var command = new RedeemPointsCommand("", PlayerProgress.MinimumRedeemablePoints);

        Assert.Contains(_validator.Validate(command), f => f.PropertyName == "OwnerId");
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenPointsIsBelowTheMinimum()
    {
        var command = new RedeemPointsCommand("USER#alice", PlayerProgress.MinimumRedeemablePoints - 1);

        Assert.Contains(_validator.Validate(command), f => f.PropertyName == "Points");
    }
}
