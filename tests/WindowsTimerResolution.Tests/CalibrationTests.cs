using Xunit;

namespace WindowsTimerResolution.Tests;

[Collection("timer")]
public class CalibrationTests
{
    private static CalibrationProbe P(uint res, double mean, double sd) =>
        new(res, mean, sd, TimerResolution.Score(mean, sd));

    [Fact]
    public void Score_PrefersCloserToOneMillisecond_AndPenalisesJitter()
    {
        Assert.True(TimerResolution.Score(1.16, 0.22) < TimerResolution.Score(1.54, 0.16));   // numbers measured on the dev machine
        Assert.True(TimerResolution.Score(1.20, 0.10) < TimerResolution.Score(1.20, 0.50));
    }

    [Fact]
    public void ChooseBest_PicksTheLowestScore()
    {
        var best = TimerResolution.ChooseBest(new[] { P(5000, 1.54, 0.16), P(5150, 1.16, 0.22), P(5300, 1.19, 0.23) });
        Assert.Equal(5150u, best.Resolution);
    }

    [Fact]
    public void ChooseBest_TieGoesToTheFinerResolution()
    {
        var best = TimerResolution.ChooseBest(new[] { P(5200, 1.2, 0.2), P(5100, 1.2, 0.2) });
        Assert.Equal(5100u, best.Resolution);
    }

    [Fact]
    public void ChooseBest_RejectsEmptyInput() =>
        Assert.Throws<ArgumentException>(() => TimerResolution.ChooseBest(Array.Empty<CalibrationProbe>()));

    [Fact]
    public void Calibrate_RejectsNonsenseParameters()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentOutOfRangeException>(() => TimerResolution.Calibrate(samples: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimerResolution.Calibrate(steps: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimerResolution.Calibrate(stepMs: 0));
    }

    [Fact]
    public void Calibrate_ReturnsAProbedValue_AndLeavesNothingHeld()
    {
        if (!OperatingSystem.IsWindows()) return;
        var range = TimerResolution.Query()!.Value;
        var result = TimerResolution.Calibrate(samples: 5, steps: 3);
        Assert.Contains(result.Probes, p => p.Resolution == result.Best);
        Assert.All(result.Probes, p => Assert.InRange(p.Resolution, range.Finest, range.Coarsest));
        Assert.False(TimerResolution.IsApplied);
    }

    [Fact]
    public void Calibrate_RestoresARequestTheCallerAlreadyHeld()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            uint held = TimerResolution.Apply();
            TimerResolution.Calibrate(samples: 5, steps: 2);
            Assert.True(TimerResolution.IsApplied);
            Assert.True(held > 0);
        }
        finally { TimerResolution.Release(); }
    }

    [Fact]
    public void OptOutOfTimerThrottling_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = TimerResolution.OptOutOfTimerThrottling();
    }
}
