using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Own2
{
    private Task<Guard> _t = Owner.EnterTaskAsync();

    public async Task Run()
    {
        var a = Owner.EnterTaskAsync();
        Unsafe.AsRef(ref a) = _t;                                        // BB01
        Guard ga = await NonCopyable.Move(ref a);                        // BB02
        var b = Owner.EnterTaskAsync();
        MemoryMarshal.CreateSpan(ref b, 1)[0] = _t;                      // BB03
        Guard gb = await NonCopyable.Move(ref b);                        // BB04
        var c = Owner.EnterTaskAsync();
        Volatile.Write(ref c, _t);                                       // BB05
        Guard gc = await NonCopyable.Move(ref c);                        // BB06
        var d = Owner.EnterTaskAsync();
        Interlocked.CompareExchange(ref d, _t, d);                       // BB07
        Guard gd = await NonCopyable.Move(ref d);                        // BB08
        var e = Owner.EnterTaskAsync();
        Unsafe.Add(ref e, 0) = _t;                                       // BB09
        Guard ge = await NonCopyable.Move(ref e);                        // BB10
        var f = Owner.EnterTaskAsync();
        ref var rf = ref Unsafe.AsRef(ref f);                            // BB11
        rf = _t;
        Guard gf = await NonCopyable.Move(ref f);                        // BB12
        var g = Owner.EnterTaskAsync();
        Pick(ref g, ref g) = _t;                                         // BB13 a user method that returns one of its ref args
        Guard gg = await NonCopyable.Move(ref g);                        // BB14
    }

    static ref Task<Guard> Pick(ref Task<Guard> x, ref Task<Guard> y) => ref x;
}
