namespace Challenges.Function.Modules.Progress.Data;

public class DynamoPlayerProgressRepository(IAmazonDynamoDB dynamoDb) : IPlayerProgressRepository
{
    public const string TableName = ProgressSchema.TableName;

    public async Task<Attempt?> GetAttemptAsync(
        OwnerId ownerId, QuestionId questionId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = Key(ownerId.Value, ProgressSchema.AttemptSortKey(questionId.Value))
            },
            cancellationToken);

        // Not present at all, or present only as a hint-tracking placeholder (no IsCorrect yet —
        // RevealHintAsync upserts this row before any answer exists, ADR-0045 §6).
        if (response.Item is not { Count: > 0 } item || !item.ContainsKey("IsCorrect"))
        {
            return null;
        }

        return MapAttempt(item);
    }

    public async Task<int> GetHintsRevealedAsync(
        OwnerId ownerId, QuestionId questionId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = Key(ownerId.Value, ProgressSchema.AttemptSortKey(questionId.Value))
            },
            cancellationToken);

        return response.Item is { Count: > 0 } item && item.TryGetValue("HintsRevealed", out var hints)
            ? int.Parse(hints.N, CultureInfo.InvariantCulture)
            : 0;
    }

    public async Task<Attempt> SaveAttemptAsync(
        OwnerId ownerId, PlayerProgress delta, CancellationToken cancellationToken = default)
    {
        var attempt = delta.Attempts.Single();
        var (profileExpression, profileValues, profileNames) = BuildProfileUpdate(delta);

        var attemptUpdate = new TransactWriteItem
        {
            Update = new Update
            {
                TableName = TableName,
                Key = Key(ownerId.Value, ProgressSchema.AttemptSortKey(attempt.Id.Value)),
                // attribute_not_exists(IsCorrect), not attribute_not_exists(SK): a hint may
                // already have created this row (RevealHintAsync upserts HintsRevealed before
                // any answer exists), so item-existence alone can't be the "already answered"
                // guard — only "not yet finalized" can (ADR-0045 §4 composed with §6).
                ConditionExpression = "attribute_not_exists(IsCorrect)",
                UpdateExpression =
                    "SET IsCorrect = :isCorrect, SelectedOption = :selected, " +
                    "PointsEarned = :pointsEarned, AnsweredAt = :answeredAt",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":isCorrect"] = new AttributeValue { BOOL = attempt.IsCorrect },
                    [":selected"] = new AttributeValue
                    {
                        N = attempt.SelectedOption.ToString(CultureInfo.InvariantCulture)
                    },
                    [":pointsEarned"] = new AttributeValue
                    {
                        N = attempt.PointsEarned.ToString(CultureInfo.InvariantCulture)
                    },
                    [":answeredAt"] = new(attempt.AnsweredAt.ToString("O"))
                }
            }
        };

        var profileUpdate = new TransactWriteItem
        {
            Update = new Update
            {
                TableName = TableName,
                Key = Key(ownerId.Value, ProgressSchema.ProfileSortKey),
                UpdateExpression = profileExpression,
                ExpressionAttributeValues = profileValues,
                ExpressionAttributeNames = profileNames
            }
        };

        var transactItems = new List<TransactWriteItem> { attemptUpdate, profileUpdate };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }, cancellationToken);

            return attempt;
        }
        catch (TransactionCanceledException ex)
            when (ConditionFailedOn(ex, transactItems.IndexOf(attemptUpdate)))
        {
            // Already scored — a double-click, a retried mutation, or a replayed request all land
            // here. Not an error: re-read and return exactly what was stored the first time
            // (ADR-0045 §4).
            return await GetAttemptAsync(ownerId, attempt.Id, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Attempt finalize for question \"{attempt.Id.Value}\" was rejected as a " +
                    "duplicate, but no already-answered attempt could be found.");
        }
    }

    public async Task<int> RevealHintAsync(
        OwnerId ownerId, QuestionId questionId, int hintCount, CancellationToken cancellationToken = default)
    {
        // A question authored with no hints is legal (AnswerKey.Of allows an empty ladder), and the
        // condition below cannot express this case: attribute_not_exists(HintsRevealed) passes on
        // the first call whatever :hintCount is, so the penalty would commit for a hint that does
        // not exist. Refuse before writing anything — the cap check further down only ever sees
        // hintCount >= 1.
        if (hintCount <= 0)
        {
            throw new HintNotAvailableException(questionId.Value);
        }

        try
        {
            var response = await dynamoDb.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = TableName,
                    Key = Key(ownerId.Value, ProgressSchema.AttemptSortKey(questionId.Value)),
                    UpdateExpression = "ADD HintsRevealed :one",
                    ConditionExpression =
                        "attribute_not_exists(IsCorrect) AND " +
                        "(attribute_not_exists(HintsRevealed) OR HintsRevealed < :hintCount)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":one"] = new AttributeValue { N = "1" },
                        [":hintCount"] = new AttributeValue
                        {
                            N = hintCount.ToString(CultureInfo.InvariantCulture)
                        }
                    },
                    ReturnValues = ReturnValue.UPDATED_NEW
                },
                cancellationToken);

            return int.Parse(response.Attributes["HintsRevealed"].N, CultureInfo.InvariantCulture);
        }
        catch (ConditionalCheckFailedException)
        {
            throw new HintNotAvailableException(questionId.Value);
        }
    }

    public async Task<Redemption> RedeemPointsAsync(
        OwnerId ownerId, PlayerProgress delta, CancellationToken cancellationToken = default)
    {
        var redemption = delta.Redemptions.Single();

        var balanceUpdate = new TransactWriteItem
        {
            Update = new Update
            {
                TableName = TableName,
                Key = Key(ownerId.Value, ProgressSchema.ProfileSortKey),
                // The only guard on the balance invariant (ADR-0046 §2) — enforced by the
                // database, not by a load-then-check in the handler, so concurrent redemptions
                // can't both pass.
                ConditionExpression = "Score >= :pts",
                UpdateExpression = "ADD Score :scoreDelta, PointsSpent :pts",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pts"] = new AttributeValue
                    {
                        N = redemption.Points.ToString(CultureInfo.InvariantCulture)
                    },
                    [":scoreDelta"] = new AttributeValue
                    {
                        N = delta.Score.ToString(CultureInfo.InvariantCulture)
                    }
                }
            }
        };

        var redemptionPut = new TransactWriteItem
        {
            Put = new Put
            {
                TableName = TableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OwnerId"] = new(ownerId.Value),
                    ["SK"] = new(ProgressSchema.RedemptionSortKey(redemption.Id)),
                    ["PointsSpent"] = new AttributeValue
                    {
                        N = redemption.Points.ToString(CultureInfo.InvariantCulture)
                    },
                    ["RedeemedAt"] = new(redemption.RedeemedAt.ToString("O"))
                },
                // A fresh Guid per Redemption.Create — this can only fail on an astronomically
                // unlikely id collision, never a legitimate replay (unlike SaveAttemptAsync,
                // there is no client-supplied idempotency key to collide on here). Deliberately
                // *not* mapped to InsufficientPointsException below: a collision here means the
                // customer had the points all along, and telling them otherwise would be a lie.
                ConditionExpression = "attribute_not_exists(SK)"
            }
        };

        var transactItems = new List<TransactWriteItem> { balanceUpdate, redemptionPut };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }, cancellationToken);

            return redemption;
        }
        catch (TransactionCanceledException ex)
            when (ConditionFailedOn(ex, transactItems.IndexOf(balanceUpdate)))
        {
            throw new InsufficientPointsException(redemption.Points);
        }
    }

    // Read consistently: both callers ask for the score immediately after the TransactWriteItems
    // that produced it, and an eventually-consistent GetItem may still serve the pre-write balance
    // — which the SPA writes straight into its score badge (ADR-0045 §10), so a correct answer
    // would appear not to have scored until the next reload.
    public async Task<int> GetScoreAsync(OwnerId ownerId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = Key(ownerId.Value, ProgressSchema.ProfileSortKey),
                ConsistentRead = true
            },
            cancellationToken);

        return response.Item is { Count: > 0 } item && item.TryGetValue("Score", out var score)
            ? int.Parse(score.N, CultureInfo.InvariantCulture)
            : 0;
    }

    // Every field on `delta` (a PlayerProgress.CreateEmpty shell with exactly one Apply call) IS
    // the amount to ADD/SET — no scoring/streak decision is made here, only a mechanical
    // translation of what PlayerProgress.Apply already decided (thin-handlers-rich-domain).
    private static (string Expression, Dictionary<string, AttributeValue> Values, Dictionary<string, string> Names)
        BuildProfileUpdate(PlayerProgress delta)
    {
        var isCorrect = delta.CorrectCount > 0;
        var values = new Dictionary<string, AttributeValue>
        {
            [":pointsEarned"] = new AttributeValue { N = delta.Score.ToString(CultureInfo.InvariantCulture) },
            [":completed"] = new AttributeValue { N = delta.Completed.ToString(CultureInfo.InvariantCulture) },
            [":hintsUsed"] = new AttributeValue { N = delta.HintsUsed.ToString(CultureInfo.InvariantCulture) },
            [":answeredAt"] = new(delta.LastAnsweredAt!.Value.ToString("O"))
        };
        var names = new Dictionary<string, string> { ["#lastAnsweredAt"] = "LastAnsweredAt" };

        var addClauses = new List<string> { "Score :pointsEarned", "Completed :completed", "HintsUsed :hintsUsed" };
        var setClauses = new List<string> { "#lastAnsweredAt = :answeredAt" };

        if (isCorrect)
        {
            values[":one"] = new AttributeValue { N = "1" };
            addClauses.Add("CorrectCount :one");
            addClauses.Add("CurrentStreak :one");
            var language = delta.ByLanguage.Keys.Single();
            names["#lang"] = ProgressSchema.LanguageAttribute(language);
            addClauses.Add("#lang :one");
        }
        else
        {
            values[":one"] = new AttributeValue { N = "1" };
            addClauses.Add("WrongCount :one");
            setClauses.Add("CurrentStreak = :zero");
            values[":zero"] = new AttributeValue { N = "0" };
        }

        var expression = $"ADD {string.Join(", ", addClauses)} SET {string.Join(", ", setClauses)}";
        return (expression, values, names);
    }

    // TransactWriteItems reports failures positionally — CancellationReasons[i] belongs to
    // TransactItems[i] — and both transactions here carry more than one conditional item, so
    // "reason[0] failed" is not the same question as "the condition I care about failed". Callers
    // pass the index of the item they're interpreting (looked up from the list, never hardcoded),
    // which keeps reordering the items from silently changing which error the customer sees.
    private static bool ConditionFailedOn(TransactionCanceledException ex, int itemIndex) =>
        itemIndex >= 0
        && ex.CancellationReasons.Count > itemIndex
        && ex.CancellationReasons[itemIndex].Code == "ConditionalCheckFailed";

    private static Dictionary<string, AttributeValue> Key(string ownerId, string sortKey) =>
        new()
        {
            ["OwnerId"] = new(ownerId),
            ["SK"] = new(sortKey)
        };

    private static Attempt MapAttempt(Dictionary<string, AttributeValue> item) =>
        Attempt.Load(
            ProgressSchema.ParseAttemptQuestionId(item["SK"].S),
            item["IsCorrect"].BOOL ?? false,
            int.Parse(item["SelectedOption"].N, CultureInfo.InvariantCulture),
            item.TryGetValue("HintsRevealed", out var hints)
                ? int.Parse(hints.N, CultureInfo.InvariantCulture)
                : 0,
            int.Parse(item["PointsEarned"].N, CultureInfo.InvariantCulture),
            DateTime.Parse(item["AnsweredAt"].S, null, DateTimeStyles.RoundtripKind));
}
