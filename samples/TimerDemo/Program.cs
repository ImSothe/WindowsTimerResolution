using System.Diagnostics;
using WindowsTimerResolution;

// Measures how long Thread.Sleep(1) really takes: default timer, a plain 0.5 ms request, and the calibrated best.
static double MeanSleepMs(int samples)
{
    var sw = new Stopwatch();
    double total = 0;
    for (int i = 0; i < samples; i++)
    {
        sw.Restart();
        Thread.Sleep(1);
        total += sw.Elapsed.TotalMilliseconds;
    }
    return total / samples;
}

var info = TimerResolution.Query();
if (info is null) { Console.WriteLine("Windows only."); return; }
Console.WriteLine($"Supported: {info.Value.FinestMs:0.000} ms (finest) .. {info.Value.CoarsestMs:0.000} ms (coarsest). In effect: {info.Value.CurrentMs:0.000} ms");

Console.WriteLine($"Sleep(1) mean, default timer   : {MeanSleepMs(200):0.00} ms");
using (TimerResolution.Hold())
    Console.WriteLine($"Sleep(1) mean, plain 0.5 ms    : {MeanSleepMs(200):0.00} ms");

Console.WriteLine("Calibrating (about 6 s, keep the machine idle)...");
var cal = TimerResolution.Calibrate();
foreach (var p in cal.Probes)
    Console.WriteLine($"  {p.ResolutionMs:0.000} ms -> mean {p.MeanMs:0.000}, sd {p.StdDevMs:0.000}{(p.Resolution == cal.Best ? "   <- best" : "")}");
using (TimerResolution.Hold(cal.Best))
    Console.WriteLine($"Sleep(1) mean, calibrated {cal.BestMs:0.000} ms: {MeanSleepMs(200):0.00} ms");
Console.WriteLine($"Sleep(1) mean, after release   : {MeanSleepMs(200):0.00} ms");
