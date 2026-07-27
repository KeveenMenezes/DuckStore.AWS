using BuildingBlocks.Core.Exceptions;
using BuildingBlocks.Core.Validation;
using BuildingBlocks.ServiceDefaults.Lambda.Behaviors;
using Mediator;

namespace BuildingBlocks.UnitTests.Behaviors;

public class ValidationBehaviorTests
{
    public sealed record TestCommand(string Value) : ICommand<Unit>;

    // Validators are plain classes now, so the tests use real ones rather than mocks —
    // there is no reflection-driven API left to stub (ADR-0042 §7).
    private sealed class FailingValidator(params string[] messages) : IValidator<TestCommand>
    {
        public IEnumerable<ValidationFailure> Validate(TestCommand instance) =>
            messages.Select(m => new ValidationFailure(nameof(TestCommand.Value), m));
    }

    private sealed class PassingValidator : IValidator<TestCommand>
    {
        public IEnumerable<ValidationFailure> Validate(TestCommand instance) => [];
    }

    [Fact]
    public async Task Handle_CallsNext_WhenNoValidatorsRegistered()
    {
        var behavior = new ValidationBehavior<TestCommand, Unit>([]);
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestCommand("anything"),
            (_, _) => { nextCalled = true; return ValueTask.FromResult(Unit.Value); },
            CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task Handle_CallsNext_WhenAllValidatorsPass()
    {
        var behavior = new ValidationBehavior<TestCommand, Unit>([new PassingValidator()]);
        var nextCalled = false;

        await behavior.Handle(
            new TestCommand("anything"),
            (_, _) => { nextCalled = true; return ValueTask.FromResult(Unit.Value); },
            CancellationToken.None);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Handle_ThrowsValidationException_WhenAValidatorFails()
    {
        var behavior = new ValidationBehavior<TestCommand, Unit>(
            [new FailingValidator("Value is required")]);

        var exception = await Assert.ThrowsAsync<ValidationException>(async () => await behavior.Handle(
            new TestCommand(""),
            (_, _) => ValueTask.FromResult(Unit.Value),
            CancellationToken.None));

        Assert.Contains(exception.Failures, f => f.ErrorMessage == "Value is required");
    }

    [Fact]
    public async Task Handle_AggregatesFailures_FromMultipleValidators()
    {
        var behavior = new ValidationBehavior<TestCommand, Unit>(
            [new FailingValidator("First failure"), new FailingValidator("Second failure")]);

        var exception = await Assert.ThrowsAsync<ValidationException>(async () => await behavior.Handle(
            new TestCommand(""),
            (_, _) => ValueTask.FromResult(Unit.Value),
            CancellationToken.None));

        Assert.Equal(2, exception.Failures.Count);
    }
}
