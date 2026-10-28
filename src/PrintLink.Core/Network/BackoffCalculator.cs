namespace PrintLink.Core.Network;

/// <summary>
/// Implements exponential backoff with full jitter for safe network reconnection.
/// Prevents thundering herd problems when servers restart or network links recover.
/// </summary>
public static class BackoffCalculator
{
    public static readonly TimeSpan DefaultBaseInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultMaxInterval = TimeSpan.FromSeconds(60);
    public const double DefaultMultiplier = 2.0;

    /// <summary>
    /// Computes the jittered delay for a given retry attempt.
    /// Formula: Sleep = UniformRandom(0, Min(MaxInterval, BaseInterval * Multiplier^attempt))
    /// </summary>
    /// <param name="attempt">Zero-based attempt counter (0, 1, 2, ...).</param>
    /// <param name="baseInterval">Initial baseline delay (default 2s).</param>
    /// <param name="maxInterval">Maximum delay ceiling (default 60s).</param>
    /// <param name="multiplier">Exponential growth factor (default 2.0).</param>
    /// <param name="random">Optional random generator for deterministic testing.</param>
    /// <returns>Calculated delay before next reconnection attempt.</returns>
    public static TimeSpan CalculateBackoff(
        int attempt,
        TimeSpan? baseInterval = null,
        TimeSpan? maxInterval = null,
        double multiplier = DefaultMultiplier,
        Random? random = null)
    {
        var baseDelay = baseInterval ?? DefaultBaseInterval;
        var maxDelay = maxInterval ?? DefaultMaxInterval;
        var rng = random ?? Random.Shared;

        if (attempt <= 0)
        {
            // First attempt delay with light initial jitter: between 0.5x and 1.0x base
            double initialJitter = 0.5 + (rng.NextDouble() * 0.5);
            return TimeSpan.FromSeconds(baseDelay.TotalSeconds * initialJitter);
        }

        // Clamp attempt exponent to avoid double overflow
        int clampedAttempt = Math.Min(attempt, 10);
        double exponentialSeconds = baseDelay.TotalSeconds * Math.Pow(multiplier, clampedAttempt);
        double ceilingSeconds = Math.Min(maxDelay.TotalSeconds, exponentialSeconds);

        // Full jitter: uniformly distributed between 0 and ceiling
        double jitteredSeconds = rng.NextDouble() * ceilingSeconds;

        // Ensure we don't fall below half of the base interval once retries are in progress
        double minFloor = baseDelay.TotalSeconds * 0.5;
        double finalSeconds = Math.Max(minFloor, jitteredSeconds);

        return TimeSpan.FromSeconds(finalSeconds);
    }
}
