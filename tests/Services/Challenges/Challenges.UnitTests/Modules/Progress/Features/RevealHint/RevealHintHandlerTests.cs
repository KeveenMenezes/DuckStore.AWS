using Challenges.Function.Modules.Progress.Features.RevealHint;
using Challenges.Function.Shared.Exceptions;

namespace Challenges.UnitTests.Modules.Progress.Features.RevealHint;

public class RevealHintHandlerTests
{
    private readonly AutoMocker _autoMocker = new();
    private readonly Mock<IQuestionRepository> _questionRepository;
    private readonly Mock<IPlayerProgressRepository> _progressRepository;
    private readonly RevealHintHandler _handler;

    public RevealHintHandlerTests()
    {
        _questionRepository = _autoMocker.GetMock<IQuestionRepository>();
        _progressRepository = _autoMocker.GetMock<IPlayerProgressRepository>();
        _handler = _autoMocker.CreateInstance<RevealHintHandler>();
    }

    private static Question ValidQuestion() =>
        Question.Create(
            QuestionId.Of("py-001"),
            "Reversed List",
            "Find the bug",
            "print('bug')",
            ["A", "B", "C"],
            Language.Of("python"),
            Difficulty.Easy,
            points: 100,
            AnswerKey.Of(0, "Because of the index.", ["hint 1", "hint 2", "hint 3"]));

    private static RevealHintCommand ValidCommand() => new("USER#alice", "py-001");

    [Fact]
    public async Task Handle_ShouldReturnTheHintAtTheNewlyRevealedPosition()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.RevealHintAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("hint 1", result.Hint);
        Assert.Equal(1, result.HintsRevealed);
        Assert.Equal(Question.HintPenalty, result.PenaltyApplied);
    }

    [Fact]
    public async Task Handle_ShouldPassTheQuestionsHintCount_AsTheCap()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.RevealHintAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        await _handler.Handle(ValidCommand(), CancellationToken.None);

        _progressRepository.Verify(
            r => r.RevealHintAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), 3, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldPropagateHintNotAvailable_WithoutReadingAHint()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.RevealHintAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HintNotAvailableException("py-001"));

        await Assert.ThrowsAsync<HintNotAvailableException>(
            () => _handler.Handle(ValidCommand(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_ShouldThrowQuestionNotFound_WhenTheQuestionDoesNotExist()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Question?)null);

        await Assert.ThrowsAsync<QuestionNotFoundException>(
            () => _handler.Handle(ValidCommand(), CancellationToken.None).AsTask());
    }
}
