using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Challenges.UnitTests.Modules.Progress.Data;

public class DynamoPlayerProgressRepositoryTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoPlayerProgressRepository _repository;
    private readonly OwnerId _ownerId = OwnerId.Of("USER#alice");

    public DynamoPlayerProgressRepositoryTests()
    {
        _repository = new DynamoPlayerProgressRepository(_dynamoDb.Object);
    }

    private static PlayerProgress CorrectDelta(OwnerId ownerId, int hintsRevealed = 0)
    {
        var progress = PlayerProgress.CreateEmpty(ownerId);
        var result = new AttemptResult(
            QuestionId.Of("py-001"), IsCorrect: true, SelectedOption: 0, hintsRevealed, PointsEarned: 100 - hintsRevealed * 25);
        progress.Apply(result, "python");
        return progress;
    }

    private static PlayerProgress WrongDelta(OwnerId ownerId)
    {
        var progress = PlayerProgress.CreateEmpty(ownerId);
        progress.Apply(new AttemptResult(QuestionId.Of("py-001"), false, 1, 0, 0), "python");
        return progress;
    }

    [Fact]
    public async Task SaveAttemptAsync_ShouldWriteTheAttemptAndFoldTheDelta_IntoProfile()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        var attempt = await _repository.SaveAttemptAsync(_ownerId, CorrectDelta(_ownerId));

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.TransactItems.Count);

        var attemptUpdate = captured.TransactItems[0].Update;
        Assert.Equal("ATTEMPT#py-001", attemptUpdate.Key["SK"].S);
        Assert.Equal("attribute_not_exists(IsCorrect)", attemptUpdate.ConditionExpression);

        var profileUpdate = captured.TransactItems[1].Update;
        Assert.Equal("PROFILE", profileUpdate.Key["SK"].S);
        Assert.Contains("Score", profileUpdate.UpdateExpression);
        Assert.Contains("CorrectCount", profileUpdate.UpdateExpression);
        Assert.Contains("CurrentStreak", profileUpdate.UpdateExpression);
        Assert.Equal(ProgressSchema.LanguageAttribute("python"), profileUpdate.ExpressionAttributeNames["#lang"]);

        Assert.True(attempt.IsCorrect);
        Assert.Equal(100, attempt.PointsEarned);
    }

    [Fact]
    public async Task SaveAttemptAsync_ShouldSetStreakToZero_WhenAnswerIsWrong()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _repository.SaveAttemptAsync(_ownerId, WrongDelta(_ownerId));

        var profileUpdate = captured!.TransactItems[1].Update;
        Assert.Contains("WrongCount", profileUpdate.UpdateExpression);
        Assert.Contains("SET #lastAnsweredAt = :answeredAt, CurrentStreak = :zero", profileUpdate.UpdateExpression);
    }

    [Fact]
    public async Task SaveAttemptAsync_ShouldReturnTheStoredAttempt_WhenTheQuestionWasAlreadyAnswered()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons = [new CancellationReason { Code = "ConditionalCheckFailed" }]
            });
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OwnerId"] = new("USER#alice"),
                    ["SK"] = new("ATTEMPT#py-001"),
                    ["IsCorrect"] = new AttributeValue { BOOL = true },
                    ["SelectedOption"] = new AttributeValue { N = "0" },
                    ["PointsEarned"] = new AttributeValue { N = "100" },
                    ["AnsweredAt"] = new(DateTime.UtcNow.ToString("O"))
                }
            });

        var attempt = await _repository.SaveAttemptAsync(_ownerId, CorrectDelta(_ownerId));

        Assert.True(attempt.IsCorrect);
        Assert.Equal(100, attempt.PointsEarned);
    }

    [Fact]
    public async Task RevealHintAsync_ShouldReturnTheNewHintsRevealedCount()
    {
        _dynamoDb
            .Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateItemResponse
            {
                Attributes = new Dictionary<string, AttributeValue> { ["HintsRevealed"] = new AttributeValue { N = "1" } }
            });

        var hintsRevealed = await _repository.RevealHintAsync(_ownerId, QuestionId.Of("py-001"), hintCount: 3);

        Assert.Equal(1, hintsRevealed);
    }

    [Fact]
    public async Task RevealHintAsync_ShouldThrowHintNotAvailable_WhenTheConditionFails()
    {
        _dynamoDb
            .Setup(d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("cap reached"));

        await Assert.ThrowsAsync<Challenges.Function.Shared.Exceptions.HintNotAvailableException>(
            () => _repository.RevealHintAsync(_ownerId, QuestionId.Of("py-001"), hintCount: 3));
    }

    [Fact]
    public async Task RevealHintAsync_ShouldRefuseWithoutWriting_WhenTheQuestionHasNoHints()
    {
        // A question authored with an empty hint ladder is legal. The cap condition can't catch
        // this — attribute_not_exists(HintsRevealed) passes on the first call whatever the cap is —
        // so without the guard the penalty commits and the handler then indexes an empty list.
        await Assert.ThrowsAsync<Challenges.Function.Shared.Exceptions.HintNotAvailableException>(
            () => _repository.RevealHintAsync(_ownerId, QuestionId.Of("py-001"), hintCount: 0));

        _dynamoDb.Verify(
            d => d.UpdateItemAsync(It.IsAny<UpdateItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetScoreAsync_ShouldReadConsistently()
    {
        // Both callers read the score one line after the transaction that produced it, and the SPA
        // writes the number straight into its badge — an eventually-consistent read can show the
        // pre-write balance, making a correct answer look like it never scored.
        GetItemRequest? captured = null;
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetItemRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new GetItemResponse
            {
                Item = new Dictionary<string, AttributeValue> { ["Score"] = new AttributeValue { N = "100" } }
            });

        var score = await _repository.GetScoreAsync(_ownerId);

        Assert.Equal(100, score);
        Assert.True(captured!.ConsistentRead);
    }

    private static PlayerProgress RedeemDelta(OwnerId ownerId, int points)
    {
        var progress = PlayerProgress.CreateEmpty(ownerId);
        progress.Redeem(points);
        return progress;
    }

    [Fact]
    public async Task RedeemPointsAsync_ShouldDebitProfile_AndWriteTheRedemptionRow()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        var redemption = await _repository.RedeemPointsAsync(_ownerId, RedeemDelta(_ownerId, 500));

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.TransactItems.Count);

        var profileUpdate = captured.TransactItems[0].Update;
        Assert.Equal("PROFILE", profileUpdate.Key["SK"].S);
        Assert.Equal("Score >= :pts", profileUpdate.ConditionExpression);
        Assert.Equal("500", profileUpdate.ExpressionAttributeValues[":pts"].N);
        Assert.Equal("-500", profileUpdate.ExpressionAttributeValues[":scoreDelta"].N);

        var redemptionPut = captured.TransactItems[1].Put;
        Assert.Equal("attribute_not_exists(SK)", redemptionPut.ConditionExpression);
        Assert.Equal(500, int.Parse(redemptionPut.Item["PointsSpent"].N));
        Assert.StartsWith("REDEMPTION#", redemptionPut.Item["SK"].S);

        Assert.Equal(500, redemption.Points);
    }

    [Fact]
    public async Task RedeemPointsAsync_ShouldThrowInsufficientPoints_WhenTheBalanceConditionFails()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons = [new CancellationReason { Code = "ConditionalCheckFailed" }]
            });

        await Assert.ThrowsAsync<Challenges.Function.Shared.Exceptions.InsufficientPointsException>(
            () => _repository.RedeemPointsAsync(_ownerId, RedeemDelta(_ownerId, 5_000)));
    }
}
