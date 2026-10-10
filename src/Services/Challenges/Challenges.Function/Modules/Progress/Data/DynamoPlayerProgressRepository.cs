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

        // Balance and ledger move together or not at all (ADR-0048 §2). Appended after the two
        // items above so their positions — which ConditionFailedOn reads by index — don't shift.
        transactItems.AddRange(delta.PointsTransactions.Select(BuildLedgerPut));

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
            // (ADR-0045 §4). Only the attempt's own condition means "replay": a conflict on the
            // ledger Put alone (a CHALLENGE# row without a finalized attempt) is an inconsistency
            // and keeps propagating instead of being swallowed.
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

    public async Task<bool> CreditAsync(PointsTransaction credit, CancellationToken cancellationToken = default)
    {
        var ledgerPut = BuildLedgerPut(credit);

        var balanceUpdate = new TransactWriteItem
        {
            Update = new Update
            {
                TableName = TableName,
                Key = Key(credit.OwnerId.Value, ProgressSchema.ProfileSortKey),
                UpdateExpression = "ADD Score :points",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":points"] = new AttributeValue { N = credit.Points.ToString(CultureInfo.InvariantCulture) }
                }
            }
        };

        var transactItems = new List<TransactWriteItem> { ledgerPut, balanceUpdate };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }, cancellationToken);

            return true;
        }
        catch (TransactionCanceledException ex)
            when (ConditionFailedOn(ex, transactItems.IndexOf(ledgerPut)))
        {
            // Already credited: the ledger row is the lock, and the balance update was cancelled
            // with it.
            return false;
        }
    }

    public async Task<PointsReservationOutcome> ReservePointsAsync(
        OwnerId ownerId, Guid orderId, int points, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var reservation = PointsTransaction.Reserve(ownerId, orderId, points, now);

        var balanceUpdate = new TransactWriteItem
        {
            Update = new Update
            {
                TableName = TableName,
                Key = Key(ownerId.Value, ProgressSchema.ProfileSortKey),
                // The balance guard, enforced by the database so concurrent checkouts can't both
                // pass. A missing PROFILE fails it too: no profile, no points.
                ConditionExpression = "Score >= :points",
                UpdateExpression = "ADD Score :debit",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":points"] = new AttributeValue { N = points.ToString(CultureInfo.InvariantCulture) },
                    [":debit"] = new AttributeValue { N = reservation.Points.ToString(CultureInfo.InvariantCulture) }
                }
            }
        };
        var ledgerPut = BuildLedgerPut(reservation);
        var transactItems = new List<TransactWriteItem> { balanceUpdate, ledgerPut };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }, cancellationToken);

            return PointsReservationOutcome.Reserved;
        }
        catch (TransactionCanceledException ex)
            when (ConditionFailedOn(ex, transactItems.IndexOf(ledgerPut)))
        {
            // Checked first: on a redelivery the balance condition may fail too (the points were
            // already debited), but the existing row is what makes it a replay.
            return PointsReservationOutcome.AlreadyProcessed;
        }
        catch (TransactionCanceledException ex)
            when (ConditionFailedOn(ex, transactItems.IndexOf(balanceUpdate)))
        {
            return await PutFailedReservationAsync(
                PointsTransaction.Fail(ownerId, orderId, points, now), cancellationToken);
        }
    }

    public async Task<PointsTransaction?> GetRedemptionAsync(
        OwnerId ownerId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = PointsTransactionsSchema.TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    [PointsTransactionsSchema.PartitionKey] = new(ownerId.Value),
                    [PointsTransactionsSchema.SortKey] = new(PointsTransactionsSchema.RedemptionTransactionId(orderId))
                },
                ConsistentRead = true
            },
            cancellationToken);

        if (response.Item is not { Count: > 0 } item)
        {
            return null;
        }

        return PointsTransaction.Load(
            ownerId,
            item[PointsTransactionsSchema.SortKey].S,
            Enum.Parse<PointsTransactionType>(item["Type"].S),
            Enum.Parse<PointsTransactionStatus>(item["Status"].S),
            int.Parse(item["Points"].N, CultureInfo.InvariantCulture),
            item["SourceId"].S,
            item.TryGetValue(PointsTransactionsSchema.OrderIdAttribute, out var order) ? order.S : null,
            DateTime.Parse(item[PointsTransactionsSchema.CreatedAtAttribute].S, null, DateTimeStyles.RoundtripKind),
            DateTime.Parse(item["UpdatedAt"].S, null, DateTimeStyles.RoundtripKind));
    }

    public async Task<bool> SaveSettlementAsync(PointsTransaction settled, CancellationToken cancellationToken = default)
    {
        var from = settled.PreviousStatus
            ?? throw new InvalidOperationException($"Points transaction \"{settled.Id}\" has no settlement to save.");

        var statusUpdate = new Update
        {
            TableName = PointsTransactionsSchema.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [PointsTransactionsSchema.PartitionKey] = new(settled.OwnerId.Value),
                [PointsTransactionsSchema.SortKey] = new(settled.Id)
            },
            // Only from the exact status the settlement starts from (ADR-0048 §5).
            ConditionExpression = "#status = :from",
            UpdateExpression = "SET #status = :to, UpdatedAt = :updatedAt",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":from"] = new(from.ToString()),
                [":to"] = new(settled.Status.ToString()),
                [":updatedAt"] = new(settled.UpdatedAt.ToString("O"))
            }
        };

        return settled.PointsToReturn == 0
            ? await UpdateStatusAsync(statusUpdate, cancellationToken)
            : await UpdateStatusAndReturnPointsAsync(statusUpdate, settled, cancellationToken);
    }

    private async Task<bool> UpdateStatusAsync(Update statusUpdate, CancellationToken cancellationToken)
    {
        try
        {
            await dynamoDb.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = statusUpdate.TableName,
                    Key = statusUpdate.Key,
                    ConditionExpression = statusUpdate.ConditionExpression,
                    UpdateExpression = statusUpdate.UpdateExpression,
                    ExpressionAttributeNames = statusUpdate.ExpressionAttributeNames,
                    ExpressionAttributeValues = statusUpdate.ExpressionAttributeValues
                },
                cancellationToken);

            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    // Status and balance move together or not at all: a returned point is never lost or given
    // twice (ADR-0048 §2).
    private async Task<bool> UpdateStatusAndReturnPointsAsync(
        Update statusUpdate, PointsTransaction settled, CancellationToken cancellationToken)
    {
        var ledgerUpdate = new TransactWriteItem { Update = statusUpdate };
        var balanceUpdate = new TransactWriteItem
        {
            Update = new Update
            {
                TableName = TableName,
                Key = Key(settled.OwnerId.Value, ProgressSchema.ProfileSortKey),
                UpdateExpression = "ADD Score :points",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":points"] = new AttributeValue { N = settled.PointsToReturn.ToString(CultureInfo.InvariantCulture) }
                }
            }
        };
        var transactItems = new List<TransactWriteItem> { ledgerUpdate, balanceUpdate };

        try
        {
            await dynamoDb.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }, cancellationToken);

            return true;
        }
        catch (TransactionCanceledException ex) when (ConditionFailedOn(ex, transactItems.IndexOf(ledgerUpdate)))
        {
            return false;
        }
    }

    private async Task<PointsReservationOutcome> PutFailedReservationAsync(
        PointsTransaction failure, CancellationToken cancellationToken)
    {
        var put = BuildLedgerPut(failure).Put;
        try
        {
            await dynamoDb.PutItemAsync(
                new PutItemRequest
                {
                    TableName = put.TableName,
                    Item = put.Item,
                    ConditionExpression = put.ConditionExpression
                },
                cancellationToken);

            return PointsReservationOutcome.Failed;
        }
        catch (ConditionalCheckFailedException)
        {
            return PointsReservationOutcome.AlreadyProcessed;
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

    private static TransactWriteItem BuildLedgerPut(PointsTransaction transaction)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            [PointsTransactionsSchema.PartitionKey] = new(transaction.OwnerId.Value),
            [PointsTransactionsSchema.SortKey] = new(transaction.Id),
            ["Type"] = new(transaction.Type.ToString()),
            ["Status"] = new(transaction.Status.ToString()),
            ["Points"] = new AttributeValue { N = transaction.Points.ToString(CultureInfo.InvariantCulture) },
            ["SourceId"] = new(transaction.SourceId),
            // "O" is fixed-width ISO-8601, so LSI1 (ordered by this attribute) sorts
            // chronologically as a plain string.
            [PointsTransactionsSchema.CreatedAtAttribute] = new(transaction.CreatedAt!.Value.ToString("O")),
            ["UpdatedAt"] = new(transaction.UpdatedAt.ToString("O"))
        };

        // Only redemptions carry it, which is what keeps GSI1 sparse (ADR-0048 §1).
        if (transaction.OrderId is not null)
        {
            item[PointsTransactionsSchema.OrderIdAttribute] = new(transaction.OrderId);
        }

        return new TransactWriteItem
        {
            Put = new Put
            {
                TableName = PointsTransactionsSchema.TableName,
                Item = item,
                // The deterministic TransactionId IS the idempotency key (ADR-0048 §1).
                ConditionExpression = $"attribute_not_exists({PointsTransactionsSchema.SortKey})"
            }
        };
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
