using BuildingBlocks.Core.CQRS;
using BuildingBlocks.ServiceDefaults.Behaviors;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace BuildingBlocks.UnitTests.Behaviors;

public class ValidationBehaviorTests
{
    public sealed record TestCommand(string Value) : ICommand<Unit>;

    [Fact]
    public async Task Handle_CallsNext_WhenNoValidatorsRegistered()
    {
        var behavior = new ValidationBehavior<TestCommand, Unit>([]);
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestCommand("anything"),
            _ => { nextCalled = true; return Task.FromResult(Unit.Value); },
            CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task Handle_CallsNext_WhenAllValidatorsPass()
    {
        var validator = new Mock<IValidator<TestCommand>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var behavior = new ValidationBehavior<TestCommand, Unit>([validator.Object]);
        var nextCalled = false;

        await behavior.Handle(
            new TestCommand("anything"),
            _ => { nextCalled = true; return Task.FromResult(Unit.Value); },
            CancellationToken.None);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Handle_ThrowsValidationException_WhenAValidatorFails()
    {
        var validator = new Mock<IValidator<TestCommand>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(
                [new ValidationFailure(nameof(TestCommand.Value), "Value is required")]));

        var behavior = new ValidationBehavior<TestCommand, Unit>([validator.Object]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(
            new TestCommand(""),
            _ => Task.FromResult(Unit.Value),
            CancellationToken.None));

        Assert.Contains(exception.Errors, e => e.ErrorMessage == "Value is required");
    }

    [Fact]
    public async Task Handle_AggregatesFailures_FromMultipleValidators()
    {
        var firstValidator = new Mock<IValidator<TestCommand>>();
        firstValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("Value", "First failure")]));

        var secondValidator = new Mock<IValidator<TestCommand>>();
        secondValidator
            .Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<TestCommand>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("Value", "Second failure")]));

        var behavior = new ValidationBehavior<TestCommand, Unit>([firstValidator.Object, secondValidator.Object]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(
            new TestCommand(""),
            _ => Task.FromResult(Unit.Value),
            CancellationToken.None));

        Assert.Equal(2, exception.Errors.Count());
    }
}
