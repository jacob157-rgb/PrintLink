using PrintLink.Core.Network;

namespace PrintLink.Tests.Core;

public class BackoffCalculatorTests
{
    [Fact]
    public void CalculateBackoff_AttemptZero_ReturnsDelayWithinInitialRange()
    {
        var baseInterval = TimeSpan.FromSeconds(2);
        var rng = new Random(42);

        var delay = BackoffCalculator.CalculateBackoff(0, baseInterval: baseInterval, random: rng);

        Assert.True(delay >= TimeSpan.FromSeconds(1.0), $"Delay {delay.TotalSeconds} should be >= 1.0s");
        Assert.True(delay <= TimeSpan.FromSeconds(2.0), $"Delay {delay.TotalSeconds} should be <= 2.0s");
    }

    [Fact]
    public void CalculateBackoff_HigherAttempts_IncreasesCeilingUpToMaxInterval()
    {
        var baseInterval = TimeSpan.FromSeconds(2);
        var maxInterval = TimeSpan.FromSeconds(30);

        for (int attempt = 1; attempt <= 15; attempt++)
        {
            var delay = BackoffCalculator.CalculateBackoff(attempt, baseInterval, maxInterval);

            // Jittered delay must never exceed maxInterval
            Assert.True(delay <= maxInterval, $"Delay {delay.TotalSeconds} exceeded max interval {maxInterval.TotalSeconds}");
            // Delay must not be negative or below floor
            Assert.True(delay >= TimeSpan.FromSeconds(1.0), $"Delay {delay.TotalSeconds} should be >= floor");
        }
    }

    [Fact]
    public void CalculateBackoff_WithDeterministicRandom_ReturnsConsistentValues()
    {
        var rng1 = new Random(12345);
        var rng2 = new Random(12345);

        var delay1 = BackoffCalculator.CalculateBackoff(3, random: rng1);
        var delay2 = BackoffCalculator.CalculateBackoff(3, random: rng2);

        Assert.Equal(delay1, delay2);
    }
}
