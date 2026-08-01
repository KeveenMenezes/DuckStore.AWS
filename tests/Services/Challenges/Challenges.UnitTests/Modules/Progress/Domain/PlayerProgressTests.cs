namespace Challenges.UnitTests.Modules.Progress.Domain;

public class PlayerProgressTests
{
    private static PlayerProgress Empty() => PlayerProgress.CreateEmpty(OwnerId.Of("USER#alice"));

    private static AttemptResult Correct(int points = 100, int hintsRevealed = 0) =>
        new(QuestionId.Of("py-001"), IsCorrect: true, SelectedOption: 0, hintsRevealed, points);

    private static AttemptResult Wrong() =>
        new(QuestionId.Of("py-001"), IsCorrect: false, SelectedOption: 1, HintsRevealed: 0, PointsEarned: 0);

    [Fact]
    public void Apply_ShouldAddPointsAndIncrementCorrectCounters_WhenCorrect()
    {
        var progress = Empty();

        progress.Apply(Correct(points: 100), "python");

        Assert.Equal(100, progress.Score);
        Assert.Equal(1, progress.Completed);
        Assert.Equal(1, progress.CorrectCount);
        Assert.Equal(0, progress.WrongCount);
        Assert.Equal(1, progress.CurrentStreak);
        Assert.Equal(1, progress.ByLanguage["python"]);
    }

    [Fact]
    public void Apply_ShouldNotAddPoints_AndResetStreak_WhenWrong()
    {
        var progress = Empty();

        progress.Apply(Wrong(), "python");

        Assert.Equal(0, progress.Score);
        Assert.Equal(1, progress.Completed);
        Assert.Equal(0, progress.CorrectCount);
        Assert.Equal(1, progress.WrongCount);
        Assert.Equal(0, progress.CurrentStreak);
        Assert.Empty(progress.ByLanguage);
    }

    [Fact]
    public void Apply_ShouldBreakTheStreak_WhenAWrongAnswerFollowsCorrectOnes()
    {
        var progress = Empty();
        progress.Apply(Correct(), "python");

        progress.Apply(Wrong(), "python");

        Assert.Equal(0, progress.CurrentStreak);
    }

    [Fact]
    public void Apply_ShouldAccumulateHintsUsed_RegardlessOfCorrectness()
    {
        var progress = Empty();

        progress.Apply(Correct(points: 100, hintsRevealed: 2), "python");

        Assert.Equal(2, progress.HintsUsed);
    }

    [Fact]
    public void Apply_ShouldReturnTheRecordedAttempt()
    {
        var progress = Empty();

        var attempt = progress.Apply(Correct(), "python");

        Assert.Same(attempt, progress.Attempts.Single());
        Assert.True(attempt.IsCorrect);
    }

    [Fact]
    public void Redeem_ShouldCarryTheNegativeScoreDelta_AndThePositivePointsSpentDelta()
    {
        var progress = Empty();

        progress.Redeem(500);

        Assert.Equal(-500, progress.Score);
        Assert.Equal(500, progress.PointsSpent);
    }

    [Fact]
    public void Redeem_ShouldReturnANewRedemption_RecordingThePointsSpent()
    {
        var progress = Empty();

        var redemption = progress.Redeem(500);

        Assert.Same(redemption, progress.Redemptions.Single());
        Assert.Equal(500, redemption.Points);
        Assert.False(string.IsNullOrWhiteSpace(redemption.Id));
    }

    [Fact]
    public void Redeem_ShouldMintADifferentId_OnEachCall()
    {
        var progress = Empty();

        var first = progress.Redeem(100);
        var second = progress.Redeem(100);

        Assert.NotEqual(first.Id, second.Id);
    }
}
