using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class RR
{
    public static ref T Reset<[NonCopyable] T>([MovedFrom] ref T v)        // J01
    {
        v = default!;
        return ref v;
    }

    public static ref T TakeInto<[NonCopyable] T>([MovedFrom] ref T v, out T taken)   // J02 not pure (out T)
    {
        taken = v;
        v = default!;
        return ref v;
    }

    public static ref readonly T Peek<[NonCopyable] T>([MovedFrom] ref T v)    // J03
    {
        T m = v;
        v = default!;
        (v, m) = (m, default!);
        return ref v;
    }

    public static ref Task<Guard> ResetTask([MovedFrom] ref Task<Guard> v)     // J04 non-generic
    {
        v = null!;
        return ref v;
    }

    public static T Pass<[NonCopyable] T>([MovedFrom] ref T v) => NonCopyable.Move(ref v);
}

public sealed class Mv3
{
    private Task<Guard> _t = Owner.EnterTaskAsync();

    public async Task Run()
    {
        var a = Owner.EnterTaskAsync();
        RR.Reset(ref a) = _t;                                    // K01
        Guard ga = await NonCopyable.Move(ref a);                // K02

        var b = Owner.EnterTaskAsync();
        ref Task<Guard> rb = ref RR.Reset(ref b);                // K03
        rb = _t;
        Guard gb = await NonCopyable.Move(ref b);                // K04

        var c = Owner.EnterTaskAsync();
        RR.ResetTask(ref c) = _t;                                // K05
        Guard gc = await NonCopyable.Move(ref c);                // K06

        var d = Owner.EnterTaskAsync();
        ref readonly Task<Guard> rd = ref RR.Peek(ref d);        // K07
        Guard gd = await NonCopyable.Move(ref d);                // K08 d holds its own task back

        var e = Owner.EnterTaskAsync();
        Guard ge = await RR.Pass(ref e);                         // K09 baseline clean
        Guard ge2 = await RR.Pass(ref e);                        // K10 e moved (null) - analyzer?
    }
}
