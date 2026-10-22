namespace BluetoothAutoReconnect.Reconnect;

/// <summary>
/// Exponential backoff with a cap and a give-up threshold. Pure: the caller tracks the attempt
/// count and resets it on a successful connect or on an advertisement from the device.
/// </summary>
public sealed record BackoffPolicy(TimeSpan InitialDelay, TimeSpan MaxDelay, int MaxAttempts)
{
    public static BackoffPolicy Default { get; } =
        new(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5), MaxAttempts: 12);

    /// <summary>Delay before attempt number <paramref name="attempt"/> (0-based), or <c>null</c> to give up.</summary>
    public TimeSpan? DelayFor(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        if (attempt >= MaxAttempts)
        {
            return null;
        }

        // Cap the exponent so the double stays finite before the MaxDelay clamp.
        var factor = Math.Pow(2, Math.Min(attempt, 30));
        var ticks = Math.Min(InitialDelay.Ticks * factor, MaxDelay.Ticks);
        return TimeSpan.FromTicks((long)ticks);
    }
}
