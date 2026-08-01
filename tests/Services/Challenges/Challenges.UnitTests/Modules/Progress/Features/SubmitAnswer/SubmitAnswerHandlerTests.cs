using Challenges.Function.Modules.Progress.Features.SubmitAnswer;

namespace Challenges.UnitTests.Modules.Progress.Features.SubmitAnswer;

public class SubmitAnswerHandlerTests
{
    private readonly AutoMocker _autoMocker = new();
    private readonly Mock<IQuestionRepository> _questionRepository;
    private readonly Mock<IPlayerProgressRepository> _progressRepository;
    private readonly SubmitAnswerHandler _handler;

    public SubmitAnswerHandlerTests()
    {
        _questionRepository = _autoMocker.GetMock<IQuestionRepository>();
        _progressRepository = _autoMocker.GetMock<IPlayerProgressRepository>();
        _handler = _autoMocker.CreateInstance<SubmitAnswerHandler>();
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
            AnswerKey.Of(0, "Because of the index.", ["hint 1", "hint 2"]));

    private static SubmitAnswerCommand ValidCommand(int selectedOption = 0) =>
        new("USER#alice", "py-001", selectedOption);

    [Fact]
    public async Task Handle_ShouldAwardFullPoints_WhenCorrectAndNoHintsUsed()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.GetHintsRevealedAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _progressRepository
            .Setup(r => r.SaveAttemptAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnerId _, PlayerProgress delta, CancellationToken _) => delta.Attempts.Single());
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsCorrect);
        Assert.Equal(100, result.PointsEarned);
        Assert.Equal(100, result.NewScore);
        Assert.Equal("Because of the index.", result.Explanation);
    }

    [Fact]
    public async Task Handle_ShouldDeductTheHintPenalty_WhenHintsWereRevealed()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.GetHintsRevealedAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _progressRepository
            .Setup(r => r.SaveAttemptAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnerId _, PlayerProgress delta, CancellationToken _) => delta.Attempts.Single());
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsCorrect);
        Assert.Equal(100 - 2 * Question.HintPenalty, result.PointsEarned);
    }

    [Fact]
    public async Task Handle_ShouldAwardZeroPoints_WhenTheAnswerIsWrong()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.GetHintsRevealedAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _progressRepository
            .Setup(r => r.SaveAttemptAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnerId _, PlayerProgress delta, CancellationToken _) => delta.Attempts.Single());
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _handler.Handle(ValidCommand(selectedOption: 1), CancellationToken.None);

        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.PointsEarned);
    }

    [Fact]
    public async Task Handle_ShouldReturnTheOriginalStoredResult_OnReplay()
    {
        // SaveAttemptAsync is itself responsible for the idempotent replay (ADR-0045 §4) — from
        // the handler's point of view it just returns whatever attempt is now stored, which may
        // not match what was just graded (e.g. a replay after other requests changed the score).
        var storedAttempt = Attempt.Load("py-001", true, 0, 0, 100, DateTime.UtcNow);
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.GetHintsRevealedAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _progressRepository
            .Setup(r => r.SaveAttemptAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedAttempt);
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var result = await _handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsCorrect);
        Assert.Equal(100, result.PointsEarned);
    }

    [Fact]
    public async Task Handle_ShouldEchoTheStoredOption_NotTheResubmittedOne_OnReplay()
    {
        // The customer first answered 0 (correct) and is now re-submitting 2. The response has to
        // describe the attempt on record, option included — a client that paints the verdict
        // against the option just clicked would tell them their correct answer was wrong.
        var storedAttempt = Attempt.Load("py-001", true, selectedOption: 0, 0, 100, DateTime.UtcNow);
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.GetHintsRevealedAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _progressRepository
            .Setup(r => r.SaveAttemptAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedAttempt);
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var result = await _handler.Handle(ValidCommand(selectedOption: 2), CancellationToken.None);

        Assert.Equal(0, result.SelectedOption);
        Assert.True(result.IsCorrect);
    }

    [Fact]
    public async Task Handle_ShouldEchoTheGradedOption_OnAFirstAttempt()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidQuestion());
        _progressRepository
            .Setup(r => r.GetHintsRevealedAsync(It.IsAny<OwnerId>(), It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _progressRepository
            .Setup(r => r.SaveAttemptAsync(It.IsAny<OwnerId>(), It.IsAny<PlayerProgress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OwnerId _, PlayerProgress delta, CancellationToken _) => delta.Attempts.Single());
        _progressRepository
            .Setup(r => r.GetScoreAsync(It.IsAny<OwnerId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _handler.Handle(ValidCommand(selectedOption: 1), CancellationToken.None);

        Assert.Equal(1, result.SelectedOption);
        Assert.False(result.IsCorrect);
    }

    [Fact]
    public async Task Handle_ShouldThrowQuestionNotFound_WhenTheQuestionDoesNotExist()
    {
        _questionRepository.Setup(r => r.GetForGradingAsync(It.IsAny<QuestionId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Question?)null);

        await Assert.ThrowsAsync<Challenges.Function.Shared.Exceptions.QuestionNotFoundException>(
            () => _handler.Handle(ValidCommand(), CancellationToken.None).AsTask());
    }
}
