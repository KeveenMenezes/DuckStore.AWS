using Challenges.Function.Modules.Progress.Features.RedeemPoints;

namespace Challenges.UnitTests.Modules.Progress.Features.RedeemPoints;

public class RedeemPointsHandlerTests
{
    private readonly AutoMocker _autoMocker = new();
    private readonly Mock<IPlayerProgressRepository> _progressRepository;
    private readonly RedeemPointsHandler _handler;

    public RedeemPointsHandlerTests()
    {
        _progressRepository = _autoMocker.GetMock<IPlayerProgressRepository>();
        _handler = _autoMocker.CreateInstance<RedeemPointsHandler>();
    }

    private static RedeemPointsCommand ValidCommand(int points = 500) => new("USER#alice", points);

    [Fact]
    public async Task Handle_ShouldReturnTheRedemptionId_AndTheNewBalance()
    {
        _progressRepository
            .Setup(r => r.RedeemPointsAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnerId _, PlayerProgress delta, CancellationToken _) => delta.Redemptions.Single());
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _handler.Handle(ValidCommand(500), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.RedemptionId));
        Assert.Equal(0, result.NewBalance);
    }

    [Fact]
    public async Task Handle_ShouldPassAPlayerProgressDelta_WithTheRequestedPointsSpent()
    {
        PlayerProgress? capturedDelta = null;
        _progressRepository
            .Setup(r => r.RedeemPointsAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .Callback<OwnerId, PlayerProgress, CancellationToken>((_, delta, _) => capturedDelta = delta)
            .ReturnsAsync((OwnerId _, PlayerProgress delta, CancellationToken _) => delta.Redemptions.Single());
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        await _handler.Handle(ValidCommand(500), CancellationToken.None);

        Assert.NotNull(capturedDelta);
        Assert.Equal(-500, capturedDelta!.Score);
        Assert.Equal(500, capturedDelta.PointsSpent);
    }

    [Fact]
    public async Task Handle_ShouldPropagateInsufficientPoints_WithoutReadingTheScoreAfterwards()
    {
        _progressRepository
            .Setup(r => r.RedeemPointsAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Challenges.Function.Shared.Exceptions.InsufficientPointsException(5_000));

        await Assert.ThrowsAsync<Challenges.Function.Shared.Exceptions.InsufficientPointsException>(
            () => _handler.Handle(ValidCommand(5_000), CancellationToken.None).AsTask());

        _progressRepository.Verify(
            r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
