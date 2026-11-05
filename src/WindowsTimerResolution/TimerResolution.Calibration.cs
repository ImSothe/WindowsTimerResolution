using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WindowsTimerResolution;

/// <summary>One candidate resolution and how <c>Sleep(1)</c> behaved while it was requested.</summary>
public readonly record struct CalibrationProbe(uint Resolution, double MeanMs, double StdDevMs, double Score)
{
    public double ResolutionMs => Resolution / 10_000.0;
}

/// <summary>Outcome of <see cref="TimerResolution.Calibrate"/>.</summary>
public sealed record CalibrationResult(uint Best, double BestMeanMs, double BaselineMeanMs, IReadOnlyList<CalibrationProbe> Probes)
{
    public double BestMs => Best / 10_000.0;
}

public static partial class TimerResolution
{
    // Windows 11 can ignore a timer request from a process it considers "background". Opting out of that is
    // documented (PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION); it is best effort and harmless where unsupported.
    private const int ProcessPowerThrottling = 4;
    private const uint IgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState { public uint Version; public uint ControlMask; public uint StateMask; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Tell Windows 11 not to ignore this process's timer requests when it has no visible window.
    /// Returns false (and does nothing) where the call is not supported.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool OptOutOfTimerThrottling()
    {
        var state = new PowerThrottlingState { Version = 1, ControlMask = IgnoreTimerResolution, StateMask = 0 };
        try { return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>()); }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>How far a candidate is from the ideal <c>Sleep(1)</c> = 1 ms. Lower is better; jitter is penalised.</summary>
    public static double Score(double meanMs, double stdDevMs) => Math.Abs(meanMs - 1.0) + 0.5 * stdDevMs;

    /// <summary>
    /// Pick the best probe. Ties go to the finer (numerically smaller) resolution. Pure function, no Windows needed.
    /// </summary>
    public static CalibrationProbe ChooseBest(IReadOnlyList<CalibrationProbe> probes)
    {
        if (probes is null || probes.Count == 0) throw new ArgumentException("No probes to choose from.", nameof(probes));
        var best = probes[0];
        foreach (var p in probes)
            if (p.Score < best.Score || (p.Score == best.Score && p.Resolution < best.Resolution)) best = p;
        return best;
    }

    /// <summary>
    /// Why this exists: asking for exactly 0.5 ms is not always what makes <c>Sleep(1)</c> most accurate. On the
    /// machine this was developed on, 0.500 ms gave a 1.54 ms mean while 0.515 ms gave 1.16 ms. The best value
    /// depends on the hardware, so measure instead of guessing.
    /// <para>
    /// Sweeps <paramref name="steps"/> candidates from the finest supported resolution upwards in
    /// <paramref name="stepMs"/> increments, measuring <paramref name="samples"/> sleeps each (about
    /// 0.5 s per candidate with the defaults). The request is released again before returning; the caller decides
    /// whether to hold the winner. Run it when the machine is idle for a clean result.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static CalibrationResult Calibrate(int samples = 25, int steps = 11, double stepMs = 0.01)
    {
        if (samples < 3) throw new ArgumentOutOfRangeException(nameof(samples), "At least 3 samples are needed.");
        if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps));
        if (stepMs <= 0) throw new ArgumentOutOfRangeException(nameof(stepMs));

        var range = Query() ?? throw new InvalidOperationException("Timer resolution is not available on this system.");
        bool wasHolding;
        uint previous;
        lock (Gate) { wasHolding = _requested != 0; previous = _requested; }
        if (wasHolding) Release();

        try
        {
            double baseline = MeanSleepMs(samples, out _);
            var probes = new List<CalibrationProbe>(steps);
            for (int i = 0; i < steps; i++)
            {
                uint candidate = range.Finest + (uint)Math.Round(i * stepMs * 10_000);
                if (candidate > range.Coarsest) break;
                Apply(candidate);
                Thread.Sleep(30);   // let the new tick rate settle before measuring
                double mean = MeanSleepMs(samples, out double sd);
                probes.Add(new CalibrationProbe(candidate, mean, sd, Score(mean, sd)));
                Release();
            }
            var best = ChooseBest(probes);
            return new CalibrationResult(best.Resolution, best.MeanMs, baseline, probes);
        }
        finally
        {
            Release();
            if (wasHolding) Apply(previous);
        }
    }

    /// <summary>Calibrate, then hold the winning resolution until disposed.</summary>
    [SupportedOSPlatform("windows")]
    public static IDisposable HoldCalibrated(out CalibrationResult result)
    {
        result = Calibrate();
        return Hold(result.Best);
    }

    private static double MeanSleepMs(int samples, out double stdDev)
    {
        var values = new List<double>(samples);
        var sw = new Stopwatch();
        for (int i = 0; i < samples + 1; i++)
        {
            sw.Restart();
            Thread.Sleep(1);
            if (i > 0) values.Add(sw.Elapsed.TotalMilliseconds);   // the first sleep lands mid-tick: discard it
            Thread.Sleep(20);
        }
        double mean = values.Average();
        stdDev = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        return mean;
    }
}
