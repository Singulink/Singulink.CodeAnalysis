using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class H4
{
    static readonly List<Task> s_all = new();
    static void Remember(Task t) => s_all.Add(t);

    public static async Task W01(Lock a, Lock b)
    {
        var first = a.EnterTaskAsync();
        var second = b.EnterTaskAsync();
        await Task.WhenAll((Task)first, second);     // W01 README idiom
        using var g1 = await first;                  // W02 read the results afterwards
        using var g2 = await second;                 // W03
    }

    public static async Task W04(Lock a, Lock b)
    {
        var first = a.EnterTaskAsync();
        var second = b.EnterTaskAsync();
        await Task.WhenAll((Task)first, second);
        using var g1 = first.Result;                 // W04
        using var g2 = second.Result;                // W05
    }

    public static async Task W06(Lock a, Lock b)
    {
        var first = a.EnterAsync();
        var second = b.EnterAsync();
        using var g1 = await first;                  // W06 sequential awaits of two value tasks: clean
        using var g2 = await second;
    }

    public static async Task O01(Lock l) { var t = l.EnterTaskAsync(); Task plain = t; var a = await t; var b = await (Task<Guard>)plain; } // O01 a reported?
    public static async Task O02(Lock l) { var t = l.EnterTaskAsync(); object o = t; var a = await t; }                                    // O02
    public static async Task O03(Lock l) { var t = l.EnterTaskAsync(); Remember(t); var a = await t; }                                     // O03
    public static async Task O04(Lock l) { var t = l.EnterTaskAsync(); var arr = new[] { t }; var a = await t; }                          // O04
    public static async Task O05(Lock l, CancellationToken ct) { var t = l.EnterTaskAsync(); using var g = await t.WaitAsync(ct); }      // O05 clean
    public static async Task O06(Lock l) { var t = l.EnterTaskAsync(); if (!t.IsCompleted) await Task.Yield(); using var g = await t; }  // O06 clean
    public static async Task O07(Lock l) { var t = l.EnterTaskAsync(); await Task.WhenAny(t, Task.Delay(100)); using var g = await t; }  // O07 timeout idiom
    public static async Task O08(Lock l) { var t = l.EnterTaskAsync(); var done = await Task.WhenAny(t, Task.Delay(100)); if (done == t) { using var g = await t; } } // O08 timeout idiom 2
    public static async Task O09(Lock l) { var t = l.EnterTaskAsync(); try { using var g = await t.WaitAsync(TimeSpan.FromSeconds(1)); } catch (TimeoutException) { } } // O09 clean
}
