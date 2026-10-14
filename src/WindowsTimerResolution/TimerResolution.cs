using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WindowsTimerResolution;

/// <summary>Timer resolution limits reported by Windows, in 100 ns units.</summary>
public readonly record struct TimerResolutionInfo(uint Coarsest, uint Finest, uint Current)
{
    public double CoarsestMs => Coarsest / 10_000.0;
    public double FinestMs => Finest / 10_000.0;
    public double CurrentMs => Current / 10_000.0;
}

/// <summary>
/// Thin, safe wrapper over <c>NtSetTimerResolution</c> / <c>NtQueryTimerResolution</c> (ntdll).
/// <para>
/// The platform timer is system-wide: while ANY process holds a request, Windows runs at the finest of
/// all active requests. A request belongs to the process and is withdrawn with the same value it was made
/// with (<c>set = false</c>). This class remembers that value so it can always release it explicitly,
/// instead of relying on Windows cleaning up when the process dies.
/// </para>
/// </summary>
public static class TimerResolution
{
    /// <summary>0.5 ms expressed in 100 ns units (the finest value most hardware supports).</summary>
    public const uint HalfMillisecond = 5_000;

    private static readonly object Gate = new();
    private static uint _requested;   // value this process asked for (0 = no active request)

    // NTSTATUS NtSetTimerResolution(ULONG DesiredResolution, BOOLEAN SetResolution, PULONG CurrentResolution)
    // BOOLEAN is 1 byte in ntdll, not the 4-byte BOOL that .NET marshals by default.
    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desired, [MarshalAs(UnmanagedType.U1)] bool set, out uint current);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);

    /// <summary>True while this process holds a request made through <see cref="Apply()"/>.</summary>
    public static bool IsApplied { get { lock (Gate) return _requested != 0; } }

    /// <summary>
    /// Query the supported range and the resolution currently in effect system-wide.
    /// Returns <c>null</c> off Windows or if the call fails.
    /// </summary>
    public static TimerResolutionInfo? Query()
    {
        if (!OperatingSystem.IsWindows()) return null;
        return NtQueryTimerResolution(out uint coarsest, out uint finest, out uint current) == 0
            ? new TimerResolutionInfo(coarsest, finest, current)
            : null;
    }

    /// <summary>
    /// Ask for 0.5 ms, or the finest the hardware supports if that is coarser. Returns the resolution
    /// actually in effect, in 100 ns units. Throws if Windows rejects the request instead of silently
    /// pretending it worked.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static uint Apply() => Apply(HalfMillisecond);

    /// <param name="desired100ns">Wanted resolution in 100 ns units. Clamped to what the hardware allows.</param>
    [SupportedOSPlatform("windows")]
    public static uint Apply(uint desired100ns)
    {
        lock (Gate)
        {
            uint desired = desired100ns;
            // The "maximum" out value of NtQueryTimerResolution is the FINEST (numerically smallest) period.
            if (NtQueryTimerResolution(out _, out uint finest, out _) == 0 && finest > 0)
                desired = Math.Max(desired100ns, finest);

            if (_requested != 0 && _requested != desired)
                NtSetTimerResolution(_requested, false, out _);   // swap: drop the old request first

            int status = NtSetTimerResolution(desired, true, out uint current);
            if (status != 0)
                throw new InvalidOperationException($"NtSetTimerResolution failed (NTSTATUS 0x{status:X8}).");
            _requested = desired;
            return current;
        }
    }

    /// <summary>Withdraw this process's request. Safe to call repeatedly, or without a prior Apply.</summary>
    [SupportedOSPlatform("windows")]
    public static void Release()
    {
        lock (Gate)
        {
            if (_requested == 0) return;
            NtSetTimerResolution(_requested, false, out _);   // set = false removes the request made with that value
            _requested = 0;
        }
    }

    /// <summary>
    /// Scope helper: <c>using (TimerResolution.Hold()) { ...latency-sensitive work... }</c>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IDisposable Hold(uint desired100ns = HalfMillisecond)
    {
        Apply(desired100ns);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int _done;

        [SupportedOSPlatform("windows")]
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) Release();
        }
    }
}
