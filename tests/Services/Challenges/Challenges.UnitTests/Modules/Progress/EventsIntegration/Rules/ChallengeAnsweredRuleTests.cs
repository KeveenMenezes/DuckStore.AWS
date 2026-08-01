using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Challenges.Function.Modules.Progress.EventsIntegration.Publishers;
using Challenges.Function.Modules.Progress.EventsIntegration.Publishers.Rules;

namespace Challenges.UnitTests.Modules.Progress.EventsIntegration.Rules;

public class ChallengeAnsweredRuleTests
{
    private static ProgressStreamImage Attempt(bool? isCorrect, int hintsRevealed = 0) =>
        new("USER#alice", "ATTEMPT#py-001", isCorrect, SelectedOption: 0, hintsRevealed, PointsEarned: 100, PointsSpent: 0);

    private static ProgressStreamImage Profile(bool? isCorrect = null) =>
        new("USER#alice", "PROFILE", isCorrect, 0, 0, 0, 0);

    [Fact]
    public void Match_ReturnsTrue_WhenAnsweredDirectly_NoPriorHint()
    {
        // INSERT with no Old image at all — answered with no hint ever revealed.
        var rule = new ChallengeAnsweredRule();
        var context = new StreamContext<ProgressStreamImage>("INSERT", null, Attempt(isCorrect: true));

        Assert.True(rule.Match(context));
    }

    [Fact]
    public void Match_ReturnsTrue_WhenFinalizedAfterAHintOnlyPlaceholder()
    {
        // MODIFY: the row already existed (a hint was revealed first), IsCorrect newly appears.
        var rule = new ChallengeAnsweredRule();
        var context = new StreamContext<ProgressStreamImage>(
            "MODIFY", Attempt(isCorrect: null, hintsRevealed: 2), Attempt(isCorrect: false, hintsRevealed: 2));

        Assert.True(rule.Match(context));
    }

    [Fact]
    public void Match_ReturnsFalse_ForAHintOnlyInsert_NotYetAnswered()
    {
        var rule = new ChallengeAnsweredRule();
        var context = new StreamContext<ProgressStreamImage>("INSERT", null, Attempt(isCorrect: null, hintsRevealed: 1));

        Assert.False(rule.Match(context));
    }

    [Fact]
    public void Match_ReturnsFalse_ForASubsequentHintReveal_StillUnanswered()
    {
        var rule = new ChallengeAnsweredRule();
        var context = new StreamContext<ProgressStreamImage>(
            "MODIFY", Attempt(isCorrect: null, hintsRevealed: 1), Attempt(isCorrect: null, hintsRevealed: 2));

        Assert.False(rule.Match(context));
    }

    [Fact]
    public void Match_ReturnsFalse_ForAnyWriteToProfile()
    {
        var rule = new ChallengeAnsweredRule();
        var insert = new StreamContext<ProgressStreamImage>("INSERT", null, Profile());
        var modify = new StreamContext<ProgressStreamImage>("MODIFY", Profile(), Profile());

        Assert.False(rule.Match(insert));
        Assert.False(rule.Match(modify));
    }

    [Fact]
    public async Task BuildAsync_BuildsChallengeAnsweredEvent_FromNewImage()
    {
        var rule = new ChallengeAnsweredRule();
        var context = new StreamContext<ProgressStreamImage>("INSERT", null, Attempt(isCorrect: true));

        var instruction = await rule.BuildAsync(context);

        Assert.Equal(nameof(ChallengeAnsweredEvent), instruction.DetailType);
        var evt = Assert.IsType<ChallengeAnsweredEvent>(instruction.Payload);
        Assert.Equal("USER#alice", evt.OwnerId);
        Assert.Equal("py-001", evt.QuestionId);
        Assert.True(evt.IsCorrect);
        Assert.Equal(100, evt.PointsEarned);
    }
}
