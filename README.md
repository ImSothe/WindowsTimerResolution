# WindowsTimerResolution

A small, dependency-free .NET 8 wrapper around `NtSetTimerResolution` / `NtQueryTimerResolution`
(ntdll) to request a finer Windows system timer, down to **0.5 ms**, and to **release it
explicitly** instead of leaving it to Windows to clean up when the process dies.

Why you might care: by default `Thread.Sleep(1)`, timers and waits on Windows are quantised to
~15.6 ms. Games, audio, real-time tooling and latency-sensitive services often need better.

```csharp
using WindowsTimerResolution;

var info = TimerResolution.Query();              // supported range + what is in effect now
Console.WriteLine($"{info?.FinestMs} ms .. {info?.CoarsestMs} ms");

using (TimerResolution.Hold())                   // asks for 0.5 ms (or the hardware's finest)
{
    // latency-sensitive work: Sleep(1) now takes ~1 ms instead of ~15.6 ms
}                                                // released here, deterministically
```

## Auto-calibration: 0.5 ms is not always the best request

```csharp
var cal = TimerResolution.Calibrate();          // ~6 s: sweeps 0.500 .. 0.600 ms and measures Sleep(1) at each step
Console.WriteLine($"best: {cal.BestMs} ms (mean Sleep(1) {cal.BestMeanMs:0.00} ms)");

using (TimerResolution.Hold(cal.Best)) { /* latency-sensitive work */ }
// or in one call:
using var h = TimerResolution.HoldCalibrated(out var result);
```

Asking for exactly 0.5 ms makes `Sleep(1)` land on the *second* tick, so it overshoots by ~0.5 ms. A request a
few hundredths of a millisecond coarser can line up better with the hardware tick. It is hardware dependent,
so the library measures instead of guessing. Score = `|mean - 1 ms| + 0.5 * stddev`, so jitter counts against a
candidate.

## Measured on my machine

Windows 11 (build 26100), supported range 0.500–15.625 ms, 200 samples each, via `samples/TimerDemo`:

| State | Mean `Sleep(1)` |
|---|---|
| Default timer | 15.58 ms |
| Plain 0.5 ms request | 1.59 ms |
| **Calibrated (0.530 ms picked on this run)** | **1.18 ms** |
| After release | 15.59 ms |

An A/B run of 4 alternating rounds x 150 samples gave **1.54 ms at 0.500 ms vs 1.16 ms at 0.515 ms** every
time. The trade-off is slightly more jitter (stddev ~0.22 ms vs ~0.16 ms). Your numbers will differ with
hardware, power plan and background load; run `Calibrate()` on an idle machine.

### What I did *not* find

Measured from a **different process**, the system clock really does drop to 0.500 ms while a helper holds the
request, but another process's `Sleep(1)` stayed at ~15.5 ms. On this Windows 11 build a request only helps the
process that made it, unless the machine opts in to the system-wide behaviour (`GlobalTimerResolutionRequests`
registry value, which needs a reboot and which I have not measured). So a resident "timer holder" process is
not a free speed-up for other programs. Call `Calibrate()`/`Hold()` inside the program that needs the timing.

## API

| Member | What it does |
|---|---|
| `Query()` | Returns `TimerResolutionInfo(Coarsest, Finest, Current)` in 100 ns units (with `*Ms` helpers), or `null` off Windows |
| `Apply()` / `Apply(uint)` | Requests a resolution (clamped to hardware limits). Returns the value actually in effect. **Throws** if Windows rejects the request, rather than pretending it worked |
| `Release()` | Withdraws this process's request. Idempotent, safe without a prior `Apply` |
| `Hold()` | `IDisposable` scope that applies now and releases on dispose (double-dispose safe) |
| `Calibrate()` / `HoldCalibrated()` | Sweeps candidate resolutions, measures `Sleep(1)` at each and returns / holds the best |
| `ChooseBest()` / `Score()` | The pure scoring logic behind calibration (testable without Windows) |
| `OptOutOfTimerThrottling()` | Asks Windows 11 not to ignore this process's timer request when it has no visible window (called automatically by `Apply`) |
| `IsApplied` | Whether this process currently holds a request |

## Things worth knowing

- **The timer is system-wide.** While *any* process holds a request, Windows runs at the finest
  active one. `Release()` removes only your own request; the resolution may stay fine if someone
  else still wants it.
- **Cost.** A finer timer wakes the CPU more often: more power on laptops. Hold it only while
  you need it (that is what `Hold()` is for).
- **Windows 10 2004+ / 11** changed how per-process requests interact with the global timer
  (a process that is minimised or has no window may not benefit). Measure on your target OS.
- The wrapper is intentionally tiny. Process-lifetime concerns (a resident helper, scheduled
  task, registry opt-in `GlobalTimerResolutionRequests`) are application decisions and are not
  included.

## Build and test

```bash
dotnet test tests/WindowsTimerResolution.Tests
dotnet run --project samples/TimerDemo
```

Tests run on Windows; off Windows the calls are skipped and `Query()` returns `null`.

## Provenance

Extracted and generalised from the timer module of Toji, a Windows tuning tool I build. I compared it against
the community reference [valleyofdoom/TimerResolution](https://github.com/valleyofdoom/TimerResolution)
(GPLv3): I read its approach and measured its binary, but copied no code, so this repo stays MIT. The idea of
scanning slightly-coarser resolutions comes from that project's `micro-adjust-benchmark.ps1`; the in-process
calibration, scoring and tests here are my own. Written with AI-assisted development; the numbers in this
README come from running the sample.

## Licence

MIT
