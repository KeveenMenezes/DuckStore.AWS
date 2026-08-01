namespace Challenges.UnitTests.Modules.Questions.Domain.Entities;

public class QuestionTests
{
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
            AnswerKey.Of(
                0,
                "Because of the index.",
                ["hint 1", "hint 2", "hint 3"]));

    [Fact]
    public void Grade_ShouldAwardFullPoints_WhenCorrectAndNoHintsUsed()
    {
        var question = ValidQuestion();

        var result = question.Grade(selectedOption: 0, hintsRevealed: 0);

        Assert.True(result.IsCorrect);
        Assert.Equal(100, result.PointsEarned);
    }

    [Fact]
    public void Grade_ShouldDeductHintPenalty_ForEachHintRevealed()
    {
        var question = ValidQuestion();

        var result = question.Grade(selectedOption: 0, hintsRevealed: 2);

        Assert.True(result.IsCorrect);
        Assert.Equal(100 - 2 * Question.HintPenalty, result.PointsEarned);
    }

    [Fact]
    public void Grade_ShouldNeverReturnNegativePoints_WhenHintPenaltyExceedsPoints()
    {
        var question = ValidQuestion();

        var result = question.Grade(selectedOption: 0, hintsRevealed: 10);

        Assert.True(result.IsCorrect);
        Assert.Equal(0, result.PointsEarned);
    }

    [Fact]
    public void Grade_ShouldAwardZeroPoints_WhenAnswerIsWrong_RegardlessOfHints()
    {
        var question = ValidQuestion();

        var result = question.Grade(selectedOption: 1, hintsRevealed: 0);

        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.PointsEarned);
    }

    [Fact]
    public void Create_ShouldThrow_WhenCorrectAnswerDoesNotIndexAnOption()
    {
        Assert.Throws<BuildingBlocks.Core.Exceptions.BadRequestException>(() => Question.Create(
            QuestionId.Of("py-001"),
            "Title",
            "Description",
            "code",
            ["A", "B"],
            Language.Of("python"),
            Difficulty.Easy,
            100,
            AnswerKey.Of(5, "explanation", ["hint"])));
    }

    [Fact]
    public void HintCount_ShouldMatchNumberOfHintsInAnswerKey()
    {
        var question = ValidQuestion();

        Assert.Equal(3, question.HintCount);
    }

    [Fact]
    public void Hint_ShouldReturnTheHintAtThatPosition()
    {
        var question = ValidQuestion();

        Assert.Equal("hint 1", question.Hint(1));
        Assert.Equal("hint 2", question.Hint(2));
    }
}
