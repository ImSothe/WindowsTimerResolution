using Xunit;

namespace WindowsTimerResolution.Tests;

// The platform timer is global, so these tests share one collection and never run in parallel.
[Collection("timer")]
public class TimerResolutionTests
{
    private static bool OnWindows => OperatingSystem.IsWindows();

    [Fact]
    public void Query_IsNullOffWindows_AndConsistentOnWindows()
    {
        var info = TimerResolution.Query();
        if (!OnWindows) { Assert.Null(info); return; }
        Assert.NotNull(info);
        Assert.True(info!.Value.Finest <= info.Value.Coarsest);   // finest = numerically smallest
        Assert.InRange(info.Value.Current, info.Value.Finest, info.Value.Coarsest);
    }

    [Fact]
    public void Apply_ReturnsTheResolutionInEffect_AndReleaseClearsTheFlag()
    {
        if (!OnWindows) return;
        try
        {
            uint current = TimerResolution.Apply();
            Assert.True(TimerResolution.IsApplied);
            Assert.True(current <= 156_250);   // never coarser than the 15.625 ms default
        }
        finally { TimerResolution.Release(); }
        Assert.False(TimerResolution.IsApplied);
    }

    [Fact]
    public void Release_WithoutApply_IsHarmlessAndIdempotent()
    {
        if (!OnWindows) return;
        TimerResolution.Release();
        TimerResolution.Release();
        Assert.False(TimerResolution.IsApplied);
    }

    [Fact]
    public void Hold_ReleasesOnDispose_EvenIfDisposedTwice()
    {
        if (!OnWindows) return;
        var h = TimerResolution.Hold();
        Assert.True(TimerResolution.IsApplied);
        h.Dispose();
        h.Dispose();
        Assert.False(TimerResolution.IsApplied);
    }

    [Fact]
    public void Apply_NeverGoesFinerThanTheHardwareAllows()
    {
        if (!OnWindows) return;
        try
        {
            var range = TimerResolution.Query()!.Value;
            uint current = TimerResolution.Apply(1);   // absurdly fine request
            Assert.True(current >= range.Finest);
        }
        finally { TimerResolution.Release(); }
    }
}
