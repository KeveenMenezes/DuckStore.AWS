using BuildingBlocks.Messaging.Events;
using BuildingBlocks.Messaging.Streams;
using Challenges.Function.Modules.Progress.EventsIntegration.Publishers;
using Challenges.Function.Modules.Progress.EventsIntegration.Publishers.Rules;

namespace Challenges.UnitTests.Modules.Progress.EventsIntegration.Rules;

public class PointsRedeemedRuleTests
{
    private static ProgressStreamImage Redemption(int pointsSpent = 500) =>
        new("USER#alice", "REDEMPTION#r-1", null, 0, 0, 0, pointsSpent);

    [Fact]
    public void Match_ReturnsTrue_ForInsertOfARedemptionRow()
    {
        var rule = new PointsRedeemedRule();
        var context = new StreamContext<ProgressStreamImage>("INSERT", null, Redemption());

        Assert.True(rule.Match(context));
    }

    [Theory]
    [InlineData("MODIFY")]
    [InlineData("REMOVE")]
    public void Match_ReturnsFalse_ForNonInsert(string eventName)
    {
        var rule = new PointsRedeemedRule();
        var redemption = Redemption();
        var context = new StreamContext<ProgressStreamImage>(eventName, redemption, redemption);

        Assert.False(rule.Match(context));
    }

    [Fact]
    public void Match_ReturnsFalse_ForInsertOfAnAttemptRow()
    {
        var rule = new PointsRedeemedRule();
        var attempt = new ProgressStreamImage("USER#alice", "ATTEMPT#py-001", true, 0, 0, 100, 0);
        var context = new StreamContext<ProgressStreamImage>("INSERT", null, attempt);

        Assert.False(rule.Match(context));
    }

    [Fact]
    public async Task BuildAsync_BuildsPointsRedeemedEvent_WithNoCurrencyField()
    {
        var rule = new PointsRedeemedRule();
        var context = new StreamContext<ProgressStreamImage>("INSERT", null, Redemption(pointsSpent: 500));

        var instruction = await rule.BuildAsync(context);

        Assert.Equal(nameof(PointsRedeemedEvent), instruction.DetailType);
        var evt = Assert.IsType<PointsRedeemedEvent>(instruction.Payload);
        Assert.Equal("USER#alice", evt.OwnerId);
        Assert.Equal("r-1", evt.RedemptionId);
        Assert.Equal(500, evt.Points);
    }
}
