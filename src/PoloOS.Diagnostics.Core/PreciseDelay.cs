using System.Diagnostics;
using System.Threading;

namespace PoloOS.Diagnostics.Core;

public static class PreciseDelay
{
    public static void Milliseconds(double milliseconds)
    {
        if (milliseconds <= 0) return;
        var targetTicks = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * milliseconds / 1000.0);
        if (milliseconds >= 4) Thread.Sleep(Math.Max(0, (int)milliseconds - 2));
        while (Stopwatch.GetTimestamp() < targetTicks) Thread.SpinWait(20);
    }
}
