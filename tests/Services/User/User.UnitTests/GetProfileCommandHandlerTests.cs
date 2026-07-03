using User.Function.Modules.Users.Data;
using User.Function.Modules.Users.Features.GetProfile;
using User.Function.Modules.Users.Models;

namespace User.UnitTests;

public class GetProfileCommandHandlerTests
{
    private const string UserId = "sub-123";

    private readonly AutoMocker _autoMocker;
    private readonly Mock<IUserProfileRepository> _repositoryMock;
    private readonly GetProfileCommandHandler _handler;

    public GetProfileCommandHandlerTests()
    {
        _autoMocker = new AutoMocker();
        _repositoryMock = _autoMocker.GetMock<IUserProfileRepository>();
        _handler = _autoMocker.CreateInstance<GetProfileCommandHandler>();
    }

    [Fact]
    public async Task Handle_ShouldProvisionProfile_WhenAbsent()
    {
        // Arrange
        _repositoryMock
            .Setup(repo => repo.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        UserProfile? stored = null;
        _repositoryMock
            .Setup(repo => repo.PutAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .Callback<UserProfile, CancellationToken>((p, _) => stored = p)
            .Returns(Task.CompletedTask);

        var command = new GetProfileCommand(UserId, "john@example.com", "John Doe");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — seeded from the claims and persisted once.
        Assert.Equal(UserId, result.UserId);
        Assert.Equal("john@example.com", result.Email);
        Assert.Equal("John Doe", result.Name);
        Assert.NotNull(stored);
        _repositoryMock.Verify(repo => repo.PutAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldReturnExisting_WithoutOverwriting()
    {
        // Arrange — an already-provisioned profile with edited fields.
        var existing = UserProfile.Load(
            UserId, "john@example.com", "John Doe", "555-1234", "1 Main St", "Springfield", "IL", "62701", "US");

        _repositoryMock
            .Setup(repo => repo.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var command = new GetProfileCommand(UserId, "john@example.com", "John Doe");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — returns stored profile and never writes.
        Assert.Equal("555-1234", result.Phone);
        Assert.Equal("Springfield", result.City);
        _repositoryMock.Verify(repo => repo.PutAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validator_ShouldHaveError_WhenUserIdOrEmailEmpty()
    {
        var validator = new GetProfileCommandValidator();

        Assert.False(validator.Validate(new GetProfileCommand("", "john@example.com", "John")).IsValid);
        Assert.False(validator.Validate(new GetProfileCommand(UserId, "", "John")).IsValid);
        Assert.True(validator.Validate(new GetProfileCommand(UserId, "john@example.com", "John")).IsValid);
    }
}
