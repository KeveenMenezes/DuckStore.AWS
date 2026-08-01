namespace BuildingBlocks.Messaging.Events;

public record ChallengeAnsweredEvent : IntegrationEvent
{
    public string OwnerId { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SelectedOption { get; set; }
    public int HintsRevealed { get; set; }
    public int PointsEarned { get; set; }
}
