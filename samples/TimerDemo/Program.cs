using System.Diagnostics;
using WindowsTimerResolution;

// Measures how long Thread.Sleep(1) really takes, with and without a 0.5 ms timer request.
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

Console.WriteLine($"Sleep(1) mean, default timer  : {MeanSleepMs(200):0.00} ms");
using (TimerResolution.Hold())
    Console.WriteLine($"Sleep(1) mean, 0.5 ms request : {MeanSleepMs(200):0.00} ms");
Console.WriteLine($"Sleep(1) mean, after release  : {MeanSleepMs(200):0.00} ms");
