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

## Measured on my machine

`samples/TimerDemo` times `Thread.Sleep(1)` (200 samples each):

| State | Mean `Sleep(1)` |
|---|---|
| Default timer | 15.74 ms |
| While holding a 0.5 ms request | 1.51 ms |
| After release | 15.73 ms |

(Windows 11, supported range 0.500–15.625 ms. Your numbers will vary with hardware and power plan.)

## API

| Member | What it does |
|---|---|
| `Query()` | Returns `TimerResolutionInfo(Coarsest, Finest, Current)` in 100 ns units (with `*Ms` helpers), or `null` off Windows |
| `Apply()` / `Apply(uint)` | Requests a resolution (clamped to hardware limits). Returns the value actually in effect. **Throws** if Windows rejects the request, rather than pretending it worked |
| `Release()` | Withdraws this process's request. Idempotent, safe without a prior `Apply` |
| `Hold()` | `IDisposable` scope that applies now and releases on dispose (double-dispose safe) |
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

Extracted and generalised from the timer module of a Windows tuning tool I build
([Sothe Windows Optimizer](https://github.com/ImSothe/Sothe-Windows-Optimizer)). Written with
AI-assisted development; the behaviour is covered by the tests above and the numbers in this
README come from running the sample.

## Licence

MIT
