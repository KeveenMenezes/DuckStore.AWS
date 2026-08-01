namespace Challenges.Function.Modules.Questions.Data;

public class DynamoQuestionRepository(IAmazonDynamoDB dynamoDb) : IQuestionRepository
{
    public const string TableName = ChallengesSchema.TableName;

    public Task AddAsync(Question question, CancellationToken cancellationToken = default) =>
        dynamoDb.TransactWriteItemsAsync(
            new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    new() { Put = new Put { TableName = TableName, Item = ToPublicItem(question) } },
                    new() { Put = new Put { TableName = TableName, Item = ToAnswerItem(question) } }
                ]
            },
            cancellationToken);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.ScanAsync(
            new ScanRequest { TableName = TableName, Select = Select.COUNT, Limit = 1 },
            cancellationToken);

        return response.Count > 0;
    }

    // One Query, not two GetItems: PUBLIC and ANSWER share the QuestionId partition, so both come
    // back in a single round trip. Both grading Lambdas (challenges-submit-answer,
    // challenges-reveal-hint) are AppSync-synchronous, so the second sequential read would be
    // latency the customer waits through for nothing.
    public async Task<Question?> GetForGradingAsync(
        QuestionId id, CancellationToken cancellationToken = default)
    {
        var response = await dynamoDb.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression = "QuestionId = :id",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":id"] = new(id.Value)
                }
            },
            cancellationToken);

        var publicItem = response.Items.FirstOrDefault(
            item => item["SK"].S == ChallengesSchema.PublicSortKey);
        var answerItem = response.Items.FirstOrDefault(
            item => item["SK"].S == ChallengesSchema.AnswerSortKey);

        // A question is only gradable with both halves present; a half-written pair is not a
        // question this can score (AddAsync writes them in one transaction, so this shouldn't happen).
        return publicItem is null || answerItem is null ? null : MapQuestion(publicItem, answerItem);
    }

    private static Dictionary<string, AttributeValue> Key(string questionId, string sortKey) =>
        new()
        {
            ["QuestionId"] = new(questionId),
            ["SK"] = new(sortKey)
        };

    private static Dictionary<string, AttributeValue> ToPublicItem(Question question) =>
        new()
        {
            ["QuestionId"] = new(question.Id.Value),
            ["SK"] = new(ChallengesSchema.PublicSortKey),
            ["Title"] = new(question.Title),
            ["Description"] = new(question.Description),
            ["Code"] = new(question.Code),
            ["Options"] = new AttributeValue { L = [.. question.Options.Select(o => new AttributeValue(o))] },
            ["Language"] = new(question.Language.Value),
            ["Difficulty"] = new(question.Difficulty.ToString()),
            ["Points"] = new AttributeValue { N = question.Points.ToString(CultureInfo.InvariantCulture) },
            ["HintCount"] = new AttributeValue { N = question.HintCount.ToString(CultureInfo.InvariantCulture) },
            // Present only here — never on the ANSWER item — so GSI1 structurally cannot surface
            // the answer key (ADR-0045 §2).
            ["GSI1PK"] = new(question.Language.Value),
            ["GSI1SK"] = new(ChallengesSchema.ComposeGsi1Sk(question.Difficulty, question.Id.Value))
        };

    private static Dictionary<string, AttributeValue> ToAnswerItem(Question question) =>
        new()
        {
            ["QuestionId"] = new(question.Id.Value),
            ["SK"] = new(ChallengesSchema.AnswerSortKey),
            ["CorrectAnswer"] = new AttributeValue
            {
                N = question.AnswerKey.CorrectAnswer.ToString(CultureInfo.InvariantCulture)
            },
            ["Explanation"] = new(question.AnswerKey.Explanation),
            ["Hints"] = new AttributeValue { L = [.. question.AnswerKey.Hints.Select(h => new AttributeValue(h))] }
        };

    private static Question MapQuestion(
        Dictionary<string, AttributeValue> publicItem, Dictionary<string, AttributeValue> answerItem) =>
        Question.Load(
            publicItem["QuestionId"].S,
            publicItem["Title"].S,
            publicItem["Description"].S,
            publicItem["Code"].S,
            publicItem.TryGetValue("Options", out var options) ? options.L.Select(o => o.S) : [],
            publicItem["Language"].S,
            Enum.Parse<Difficulty>(publicItem["Difficulty"].S),
            int.Parse(publicItem["Points"].N, CultureInfo.InvariantCulture),
            AnswerKey.Of(
                int.Parse(answerItem["CorrectAnswer"].N, CultureInfo.InvariantCulture),
                answerItem["Explanation"].S,
                answerItem.TryGetValue("Hints", out var hints) ? hints.L.Select(h => h.S) : []));
}
