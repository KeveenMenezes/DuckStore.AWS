namespace Payment.UnitTests.DataTests;

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    // 2026-10-15: card expiration tests reason about "this month" being 10/2026.
    public static readonly FixedClock October2026 = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => now;
}
