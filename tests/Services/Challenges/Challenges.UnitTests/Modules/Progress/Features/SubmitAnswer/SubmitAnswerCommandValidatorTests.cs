using Challenges.Function.Modules.Progress.Features.SubmitAnswer;

namespace Challenges.UnitTests.Modules.Progress.Features.SubmitAnswer;

public class SubmitAnswerCommandValidatorTests
{
    private readonly SubmitAnswerCommandValidator _validator = new();

    [Fact]
    public void Validate_ShouldHaveNoErrors_ForAValidCommand()
    {
        var command = new SubmitAnswerCommand("USER#alice", "py-001", 0);

        Assert.Empty(_validator.Validate(command));
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenChallengeIdIsMissing()
    {
        var command = new SubmitAnswerCommand("USER#alice", "", 0);

        Assert.Contains(_validator.Validate(command), f => f.PropertyName == "ChallengeId");
    }

    [Fact]
    public void Validate_ShouldHaveError_WhenSelectedOptionIsNegative()
    {
        var command = new SubmitAnswerCommand("USER#alice", "py-001", -1);

        Assert.Contains(_validator.Validate(command), f => f.PropertyName == "SelectedOption");
    }
}
