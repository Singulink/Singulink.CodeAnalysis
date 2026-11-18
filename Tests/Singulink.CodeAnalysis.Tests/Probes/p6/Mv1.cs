using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class PM<[NonCopyable] T>
{
    public static T s_slot = default!;

    // A: overwrite the parameter through a non-pure helper with a ref parameter, then move it out
    public static T ViaPut([MovedFrom] ref T v)
    {
        Helpers.Put(ref v, ref s_slot);            // A01
        return NonCopyable.Move(ref v);            // A02
    }

    public static T ViaSwap([MovedFrom] ref T v)
    {
        Helpers.Swap2(ref v, ref s_slot);          // A03
        return NonCopyable.Move(ref v);            // A04
    }

    public static T ViaOut([MovedFrom] ref T v)
    {
        Helpers.Fetch(out v);                      // A05
        return NonCopyable.Move(ref v);            // A06
    }

    public static T ViaRefLocal([MovedFrom] ref T v)
    {
        ref T r = ref v;                           // A07
        r = NonCopyable.Move(ref s_slot);          // A08
        return NonCopyable.Move(ref v);            // A09
    }

    public static T ViaRefReadonly([MovedFrom] ref T v)
    {
        ref readonly T r = ref v;                  // A10
        Unsafe.AsRef(in r) = NonCopyable.Move(ref s_slot);  // A11
        return NonCopyable.Move(ref v);            // A12
    }

    public static T ViaUnsafeAs([MovedFrom] ref T v)
    {
        Unsafe.As<T, T>(ref v) = NonCopyable.Move(ref s_slot);   // A13
        return NonCopyable.Move(ref v);            // A14
    }

    public static T ViaInterlocked([MovedFrom] ref T v)
    {
        T old = NonCopyable.Move(ref v);           // A15
        v = default!;
        Helpers.Swap2(ref v, ref s_slot);          // A16  leaves stored in v after reset
        return old;
    }

    public static T ViaLocalSwap([MovedFrom] ref T v)
    {
        T mine = default!;
        Helpers.Swap2(ref mine, ref s_slot);        // A17 local written through ref: no longer pure?
        v = NonCopyable.Move(ref mine);             // A18
        return default!;
    }

    public static T ViaLocalOut([MovedFrom] ref T v)
    {
        T old = NonCopyable.Move(ref v);
        Helpers.Fetch(out T other);                // A19
        v = NonCopyable.Move(ref other);           // A20
        return old;
    }

    public static T ViaSpan([MovedFrom] ref T v)
    {
        var span = new Span<T>(ref v);             // A21
        span[0] = NonCopyable.Move(ref s_slot);    // A22
        return NonCopyable.Move(ref v);            // A23
    }

    public static T ViaLambda([MovedFrom] ref T v)
    {
        T moved = NonCopyable.Move(ref v);
        return Run(() => NonCopyable.Move(ref s_slot));    // A24
        static T Run(Func<T> f) => f();
    }

    public static T ViaLocalFunction([MovedFrom] ref T v)
    {
        Overwrite(ref v);                          // A25
        return NonCopyable.Move(ref v);            // A26
        static void Overwrite(ref T x) => x = NonCopyable.Move(ref s_slot);
    }

    public static T ViaTupleDeconstruct([MovedFrom] ref T v)
    {
        (T a, int n) = (NonCopyable.Move(ref s_slot), 1);  // A27
        v = default!;
        return NonCopyable.Move(ref a);            // A28
    }

    public static T ViaThrowFinally([MovedFrom] ref T v)
    {
        try { return NonCopyable.Move(ref v); }    // A29
        finally { v = NonCopyable.Move(ref s_slot); }   // A30
    }

    public static ref T ViaRefReturn([MovedFrom] ref T v)   // A31 ref return of the parameter
    {
        return ref v;
    }

    public static ref readonly T ViaRefReadonlyReturn([MovedFrom] ref T v)   // A32
    {
        return ref v;
    }
}

public sealed class Mv1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();

    public async Task Run()
    {
        Stash<Task<Guard>>.Store(_t);                                     // B01 storing is fine
        var x = Owner.EnterTaskAsync();
        Guard a = await PM<Task<Guard>>.ViaPut(ref x);                    // B02
        PM<Task<Guard>>.s_slot = _t;                                      // B03
        var y = Owner.EnterTaskAsync();
        Guard b = await PM<Task<Guard>>.ViaRefLocal(ref y);               // B04
        var z = Owner.EnterTaskAsync();
        ref readonly Task<Guard> rz = ref PM<Task<Guard>>.ViaRefReadonlyReturn(ref z); // B05
        Guard c = await NonCopyable.Move(ref Unsafe.AsRef(in rz));        // B06
        var w = Owner.EnterTaskAsync();
        ref Task<Guard> rw = ref PM<Task<Guard>>.ViaRefReturn(ref w);     // B07
        rw = _t;                                                          // B08
        Guard d = await NonCopyable.Move(ref w);                          // B09
        var u = Owner.EnterTaskAsync();
        Guard e = await PM<Task<Guard>>.ViaOut(ref u);                    // B10
    }
}
