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
        Assert.Equal(3, captured!.TransactItems.Count);

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
    public async Task SaveAttemptAsync_ShouldPutTheLedgerCredit_InTheSameTransaction_WhenCorrect()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _repository.SaveAttemptAsync(_ownerId, CorrectDelta(_ownerId, hintsRevealed: 1));

        // Attempt, balance and ledger row commit or fail together (ADR-0048 §2).
        var put = captured!.TransactItems[2].Put;
        Assert.NotNull(put);
        Assert.Equal(PointsTransactionsSchema.TableName, put.TableName);
        Assert.Equal("attribute_not_exists(TransactionId)", put.ConditionExpression);
        Assert.Equal("USER#alice", put.Item["OwnerId"].S);
        Assert.Equal("CHALLENGE#py-001", put.Item["TransactionId"].S);
        Assert.Equal("ChallengeCredit", put.Item["Type"].S);
        Assert.Equal("Completed", put.Item["Status"].S);
        Assert.Equal("75", put.Item["Points"].N);
        Assert.Equal("py-001", put.Item["SourceId"].S);
        Assert.Equal(put.Item["CreatedAt"].S, put.Item["UpdatedAt"].S);
        Assert.False(put.Item.ContainsKey("OrderId"));
    }

    [Fact]
    public async Task SaveAttemptAsync_ShouldNotTouchTheLedger_WhenWrong()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _repository.SaveAttemptAsync(_ownerId, WrongDelta(_ownerId));

        Assert.Equal(2, captured!.TransactItems.Count);
        Assert.All(captured.TransactItems, i => Assert.Null(i.Put));
    }

    [Fact]
    public async Task SaveAttemptAsync_ShouldNotTreatALedgerOnlyConflict_AsAReplay()
    {
        // The attempt condition passed (reason[0] is OK) and only the ledger row collided. That is
        // not the idempotent re-submission of ADR-0045 §4 — swallowing it would hide a ledger that
        // disagrees with the attempts — so it must surface, not be mapped to a stored attempt.
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons =
                [
                    new CancellationReason { Code = "None" },
                    new CancellationReason { Code = "None" },
                    new CancellationReason { Code = "ConditionalCheckFailed" }
                ]
            });

        await Assert.ThrowsAsync<TransactionCanceledException>(
            () => _repository.SaveAttemptAsync(_ownerId, CorrectDelta(_ownerId)));

        _dynamoDb.Verify(
            d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveAttemptAsync_ShouldStillTreatAReplay_AsANoOp_NowThatTheTransactionHasThreeItems()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons =
                [
                    new CancellationReason { Code = "ConditionalCheckFailed" },
                    new CancellationReason { Code = "None" },
                    new CancellationReason { Code = "ConditionalCheckFailed" }
                ]
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

    private static readonly Guid ReviewedProduct = Guid.Parse("6755192f-0676-413c-8358-7d70c118fc2a");

    private PointsTransaction ReviewCredit() =>
        PointsTransaction.ReviewCredit(_ownerId, ReviewedProduct, new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task CreditAsync_ShouldPutTheLedgerRow_AndAddToScore_InOneTransaction()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        var credited = await _repository.CreditAsync(ReviewCredit());

        Assert.True(credited);
        Assert.Equal(2, captured!.TransactItems.Count);

        var put = captured.TransactItems.Single(i => i.Put is not null).Put;
        Assert.Equal(PointsTransactionsSchema.TableName, put.TableName);
        Assert.Equal("attribute_not_exists(TransactionId)", put.ConditionExpression);
        Assert.Equal("USER#alice", put.Item["OwnerId"].S);
        Assert.Equal($"REVIEW#{ReviewedProduct}", put.Item["TransactionId"].S);
        Assert.Equal("ReviewCredit", put.Item["Type"].S);
        Assert.Equal("Completed", put.Item["Status"].S);
        Assert.Equal("15", put.Item["Points"].N);

        // A player who never answered a challenge has no PROFILE yet: ADD creates it, and every
        // reader treats the absent counters as 0.
        var profileUpdate = captured.TransactItems.Single(i => i.Update is not null).Update;
        Assert.Equal(ProgressSchema.TableName, profileUpdate.TableName);
        Assert.Equal("PROFILE", profileUpdate.Key["SK"].S);
        Assert.Equal("USER#alice", profileUpdate.Key["OwnerId"].S);
        Assert.Equal("ADD Score :points", profileUpdate.UpdateExpression);
        Assert.Equal("15", profileUpdate.ExpressionAttributeValues[":points"].N);
        Assert.Null(profileUpdate.ConditionExpression);
    }

    [Fact]
    public async Task CreditAsync_ShouldReturnFalse_WhenTheLedgerRowAlreadyExists()
    {
        // Redelivered event, or the review was deleted and published again: the deterministic
        // REVIEW#<productId> row is already there, so the balance is left untouched.
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons =
                [
                    new CancellationReason { Code = "ConditionalCheckFailed" },
                    new CancellationReason { Code = "None" }
                ]
            });

        Assert.False(await _repository.CreditAsync(ReviewCredit()));
    }

    [Fact]
    public async Task CreditAsync_ShouldPropagate_WhenTheTransactionFailsForAnyOtherReason()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransactionCanceledException("cancelled")
            {
                CancellationReasons =
                [
                    new CancellationReason { Code = "None" },
                    new CancellationReason { Code = "TransactionConflict" }
                ]
            });

        await Assert.ThrowsAsync<TransactionCanceledException>(() => _repository.CreditAsync(ReviewCredit()));
    }

    private static readonly Guid CheckedOutOrder = Guid.Parse("b91258ab-9298-43ae-9ee0-b65752590ec4");

    private static TransactionCanceledException Cancelled(string balanceCode, string ledgerCode) =>
        new("cancelled")
        {
            CancellationReasons =
            [
                new CancellationReason { Code = balanceCode },
                new CancellationReason { Code = ledgerCode }
            ]
        };

    [Fact]
    public async Task ReservePointsAsync_ShouldDebitUnderTheBalanceGuard_AndPutTheReservedRow()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        var outcome = await _repository.ReservePointsAsync(_ownerId, CheckedOutOrder, 1000);

        Assert.Equal(PointsReservationOutcome.Reserved, outcome);
        var balance = captured!.TransactItems[0].Update;
        Assert.Equal("PROFILE", balance.Key["SK"].S);
        Assert.Equal("Score >= :points", balance.ConditionExpression);
        Assert.Equal("ADD Score :debit", balance.UpdateExpression);
        Assert.Equal("1000", balance.ExpressionAttributeValues[":points"].N);
        Assert.Equal("-1000", balance.ExpressionAttributeValues[":debit"].N);

        var put = captured.TransactItems[1].Put;
        Assert.Equal(PointsTransactionsSchema.TableName, put.TableName);
        Assert.Equal("attribute_not_exists(TransactionId)", put.ConditionExpression);
        Assert.Equal($"REDEMPTION#{CheckedOutOrder}", put.Item["TransactionId"].S);
        Assert.Equal("Reserved", put.Item["Status"].S);
        Assert.Equal("-1000", put.Item["Points"].N);
        Assert.Equal(CheckedOutOrder.ToString(), put.Item["OrderId"].S);
    }

    [Fact]
    public async Task ReservePointsAsync_ShouldPutAFailedRow_WithoutDebiting_WhenTheBalanceIsShort()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Cancelled("ConditionalCheckFailed", "None"));
        PutItemRequest? failedPut = null;
        _dynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutItemRequest, CancellationToken>((r, _) => failedPut = r)
            .ReturnsAsync(new PutItemResponse());

        var outcome = await _repository.ReservePointsAsync(_ownerId, CheckedOutOrder, 1000);

        Assert.Equal(PointsReservationOutcome.Failed, outcome);
        Assert.NotNull(failedPut);
        Assert.Equal(PointsTransactionsSchema.TableName, failedPut!.TableName);
        Assert.Equal("attribute_not_exists(TransactionId)", failedPut.ConditionExpression);
        Assert.Equal("Failed", failedPut.Item["Status"].S);
        Assert.Equal($"REDEMPTION#{CheckedOutOrder}", failedPut.Item["TransactionId"].S);
        Assert.Equal(CheckedOutOrder.ToString(), failedPut.Item["OrderId"].S);
    }

    // A redelivered BasketCheckoutEvent: the REDEMPTION# row is already there, whatever the balance
    // condition says now (it was debited the first time), so nothing more is written.
    [Theory]
    [InlineData("None")]
    [InlineData("ConditionalCheckFailed")]
    public async Task ReservePointsAsync_ShouldBeANoOp_WhenTheOrderWasAlreadyProcessed(string balanceCode)
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Cancelled(balanceCode, "ConditionalCheckFailed"));

        var outcome = await _repository.ReservePointsAsync(_ownerId, CheckedOutOrder, 1000);

        Assert.Equal(PointsReservationOutcome.AlreadyProcessed, outcome);
        _dynamoDb.Verify(
            d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReservePointsAsync_ShouldBeANoOp_WhenTheFailedRowWasAlreadyWritten()
    {
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(Cancelled("ConditionalCheckFailed", "None"));
        _dynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("exists"));

        var outcome = await _repository.ReservePointsAsync(_ownerId, CheckedOutOrder, 1000);

        Assert.Equal(PointsReservationOutcome.AlreadyProcessed, outcome);
    }
}
