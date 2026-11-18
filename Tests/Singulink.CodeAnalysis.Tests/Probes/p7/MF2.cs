using System;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class MF2
{
    // reads t, then takes the same task out again with Exchange, and puts it back
    public static Task<Guard> Dup([MovedFrom] ref Task<Guard> t)    // D01
    {
        var m = t;
        var n = Interlocked.Exchange(ref t, null!);
        t = n;
        return m;
    }

    // the same with the result of Exchange returned and the first read put back
    public static Task<Guard> Dup2([MovedFrom] ref Task<Guard> t)   // D02
    {
        var m = t;
        var n = Interlocked.Exchange(ref t, m);
        return n;
    }
}

public static class RTMF2
{
    public static Lock D01() => Run(1);
    public static Lock D02() => Run(2);

    static Lock Run(int k)
    {
        var l = new Lock();
        RunAsync(l, k).GetAwaiter().GetResult();
        return l;
    }

    static async Task RunAsync(Lock l, int k)
    {
        var t = l.EnterTaskAsync();
        var a = k == 1 ? MF2.Dup(ref t) : MF2.Dup2(ref t);   // D03
        using Guard g1 = await a;                             // D04
        using Guard g2 = await t;                             // D05
    }
}
