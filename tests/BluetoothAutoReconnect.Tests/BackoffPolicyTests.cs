using BluetoothAutoReconnect.Reconnect;

namespace BluetoothAutoReconnect.Tests;

public class BackoffPolicyTests
{
    private static readonly BackoffPolicy Policy =
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), MaxAttempts: 5);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 10)]
    public void DoublesUntilCapped(int attempt, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Policy.DelayFor(attempt));

    [Fact]
    public void GivesUpAfterMaxAttempts() => Assert.Null(Policy.DelayFor(5));

    [Fact]
    public void LargeAttemptCountsDoNotOverflow()
    {
        var policy = Policy with { MaxAttempts = int.MaxValue };
        Assert.Equal(TimeSpan.FromSeconds(10), policy.DelayFor(10_000));
    }
}
