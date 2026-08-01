using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace Challenges.UnitTests.Modules.Questions.Data;

public class DynamoQuestionRepositoryTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private readonly DynamoQuestionRepository _repository;

    public DynamoQuestionRepositoryTests()
    {
        _repository = new DynamoQuestionRepository(_dynamoDb.Object);
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

    [Fact]
    public async Task AddAsync_ShouldWriteBothItems_InOneTransaction()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _repository.AddAsync(ValidQuestion());

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.TransactItems.Count);
        Assert.All(captured.TransactItems, item => Assert.Equal("challenges", item.Put.TableName));
    }

    [Fact]
    public async Task AddAsync_ShouldNeverPutGsiAttributes_OnTheAnswerItem()
    {
        TransactWriteItemsRequest? captured = null;
        _dynamoDb
            .Setup(d => d.TransactWriteItemsAsync(It.IsAny<TransactWriteItemsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TransactWriteItemsRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TransactWriteItemsResponse());

        await _repository.AddAsync(ValidQuestion());

        var publicItem = captured!.TransactItems.Single(i => i.Put.Item["SK"].S == "PUBLIC").Put.Item;
        var answerItem = captured.TransactItems.Single(i => i.Put.Item["SK"].S == "ANSWER").Put.Item;

        Assert.True(publicItem.ContainsKey("GSI1PK"));
        Assert.True(publicItem.ContainsKey("GSI1SK"));
        Assert.False(answerItem.ContainsKey("GSI1PK"));
        Assert.False(answerItem.ContainsKey("GSI1SK"));

        // The answer key never appears on the public item either.
        Assert.False(publicItem.ContainsKey("CorrectAnswer"));
        Assert.False(publicItem.ContainsKey("Explanation"));
        Assert.False(publicItem.ContainsKey("Hints"));
    }

    private static Dictionary<string, AttributeValue> PublicItem() =>
        new()
        {
            ["QuestionId"] = new("py-001"),
            ["SK"] = new("PUBLIC"),
            ["Title"] = new("Reversed List"),
            ["Description"] = new("Find the bug"),
            ["Code"] = new("print('bug')"),
            ["Options"] = new AttributeValue { L = [new("A"), new("B")] },
            ["Language"] = new("python"),
            ["Difficulty"] = new(nameof(Difficulty.Easy)),
            ["Points"] = new AttributeValue { N = "100" },
            ["HintCount"] = new AttributeValue { N = "2" }
        };

    private static Dictionary<string, AttributeValue> AnswerItem() =>
        new()
        {
            ["QuestionId"] = new("py-001"),
            ["SK"] = new("ANSWER"),
            ["CorrectAnswer"] = new AttributeValue { N = "0" },
            ["Explanation"] = new("Because of the index."),
            ["Hints"] = new AttributeValue { L = [new("hint 1"), new("hint 2")] }
        };

    private void SetupQuery(params Dictionary<string, AttributeValue>[] items) =>
        _dynamoDb
            .Setup(d => d.QueryAsync(
                It.Is<QueryRequest>(r => r.ExpressionAttributeValues[":id"].S == "py-001"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = [.. items] });

    [Fact]
    public async Task GetForGradingAsync_ShouldReadBothItems_InASingleQuery()
    {
        QueryRequest? captured = null;
        _dynamoDb
            .Setup(d => d.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<QueryRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new QueryResponse { Items = [PublicItem(), AnswerItem()] });

        await _repository.GetForGradingAsync(QuestionId.Of("py-001"));

        Assert.NotNull(captured);
        Assert.Equal("challenges", captured!.TableName);
        Assert.Equal("QuestionId = :id", captured.KeyConditionExpression);
        // PUBLIC and ANSWER share the partition; grading is on the synchronous path of two
        // AppSync-backed Lambdas, so it must never pay for a second round trip.
        _dynamoDb.Verify(
            d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetForGradingAsync_ShouldReturnNull_WhenPublicItemIsMissing()
    {
        SetupQuery(AnswerItem());

        var result = await _repository.GetForGradingAsync(QuestionId.Of("py-001"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetForGradingAsync_ShouldReturnNull_WhenAnswerItemIsMissing()
    {
        SetupQuery(PublicItem());

        var result = await _repository.GetForGradingAsync(QuestionId.Of("py-001"));

        Assert.Null(result);
    }

    [Fact]
    public async Task GetForGradingAsync_ShouldHydrateAFullyGradableQuestion()
    {
        SetupQuery(PublicItem(), AnswerItem());

        var question = await _repository.GetForGradingAsync(QuestionId.Of("py-001"));

        Assert.NotNull(question);
        var result = question!.Grade(selectedOption: 0, hintsRevealed: 0);
        Assert.True(result.IsCorrect);
        Assert.Equal(100, result.PointsEarned);
        Assert.Equal("Because of the index.", question.Explanation);
        Assert.Equal("hint 1", question.Hint(1));
    }
}
