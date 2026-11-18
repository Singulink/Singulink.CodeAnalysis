using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Singulink.CodeAnalysis;

[NonCopyable] public struct G3 : IDisposable
{
    private int _state;
    [UnscopedRef] public ref G3 Self => ref this;
    [UnscopedRef] public Span<int> State => new Span<int>(ref _state);
    public void Dispose() => _state = 0;
}

ref struct RefHolder
{
    public ref Guard R;
    public void Set(ref Guard g) => R = ref g;                 // compiles?
}

static class C1
{
    static Guard s_field;
    static G3 s_g3;
    static ref readonly Guard Echo(in Guard g) => ref g;

    public static void A()
    {
        (Guard, int) t = (Owner.Enter(), 0);
        ref Guard part = ref t.Item1;      // J01 a ref to a part
        s_field = t.Item1;                 // (by-value read of a part)
        part.Dispose();
    }

    public static void B()
    {
        G3 g = default;
        ref G3 alias = ref g.Self;         // J02 a call on g that returns a reference into it
        s_g3 = g;                          // J03 a last use by name
        alias.Dispose();
    }

    public static void C()
    {
        Guard g = Owner.Enter();
        RefHolder h = default;
        h.Set(ref g);                      // J04 receiver is a ref struct
        s_field = g;                       // J05
        h.R.Dispose();
    }

    public static void D()
    {
        Guard g = Owner.Enter();
        ref readonly Guard r = ref Echo(g);   // J06 by reference without a modifier
        s_field = g;                          // J07
    }

    public static void E()
    {
        Guard g = Owner.Enter();
        TypedReference tr = __makeref(g);     // J08
        s_field = g;                          // J09
        __refvalue(tr, Guard).Dispose();
    }
}
