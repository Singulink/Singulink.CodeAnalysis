using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class RT2
{
    static Memory<Guard> s_mem;
    static Task<Guard>? s_w;
    static Task<Guard>? s_pending;

    static Guard[] MakeShared(Lock l)
    {
        var a = new Guard[1];
        a[0] = l.Enter();
        s_mem = a.AsMemory();
        return a;                                                       // X01
    }

    public static Lock X01()
    {
        var l = new Lock();
        IList<Guard> il = MakeShared(l);                                // X02
        Guard g = il[0];                                                // X03
        g.Dispose();
        s_mem.Span[0].Dispose();
        return l;
    }

    static (Task<Guard>, Task<Guard>) Two(Lock l)
    {
        var t = l.EnterTaskAsync();
        return (t, t.WaitAsync(CancellationToken.None));                // X04
    }

    static Task<Guard> One(Lock l)
    {
        var t = l.EnterTaskAsync();
        s_w = t.WaitAsync(CancellationToken.None);                      // X05
        return t;                                                       // X06
    }

    static Task<Guard> Take() => Interlocked.Exchange(ref s_pending, null)!;   // X07

    static async Task<Lock> X08a()
    {
        var l = new Lock();
        var (a, b) = Two(l);
        Guard g1 = await a;                                             // X08
        Guard g2 = await b;                                             // X09
        g1.Dispose(); g2.Dispose();
        return l;
    }
    public static Lock X08() => X08a().Result;

    static async Task<Lock> X10a()
    {
        var l = new Lock();
        Guard g1 = await One(l);                                        // X10
        Guard g2 = await Interlocked.Exchange(ref s_w, null)!;          // X11
        g1.Dispose(); g2.Dispose();
        return l;
    }
    public static Lock X10() => X10a().Result;

    static Task<Guard> Wrap(Lock l)
    {
        var t = l.EnterTaskAsync();
        var w = t.WaitAsync(CancellationToken.None);
        s_pending = t;                                                  // X12
        return w;                                                       // X13
    }

    static async Task<Lock> X14a()
    {
        var l = new Lock();
        Guard g1 = await Wrap(l);                                       // X14
        Guard g2 = await Take();                                        // X15
        g1.Dispose(); g2.Dispose();
        return l;
    }
    public static Lock X14() => X14a().Result;
}
