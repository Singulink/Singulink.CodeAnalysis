using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

class H4
{
    public List<Guard> _l = new();
    [return: Borrowed] public List<Guard> Get() => _l;                       // E00 OK
    public List<Guard> Make() => new();
    public static void Sink(object o) { }
}

[NonCopyable] struct S4<[NonCopyable] T> where T : IDisposable
{
    private T _v;
    private Guard _g;
    public readonly void Close() => _v.Dispose();                            // E20 copy of _v (readonly member)
    public readonly void Touch() => _g.Touch();                              // E21 copy of _g
    public void Ok() => _g.Touch();                                          // E22 fine
    public readonly bool Held => _g.IsHeld;                                  // E23 fine (readonly member)
}

static class Ext4
{
    extension(Guard g)
    {
        public bool HeldX => g.IsHeld;                                       // E30 decl
        public void TouchX() => g.Touch();
    }
    public static void TouchByValue(this Guard g) => g.Touch();
}

class P4
{
    private Guard _g;
    private readonly Guard _rg;

    void M(H4 h, Guard? maybe)
    {
        Func<List<Guard>> f = h.Get;                                         // E01 SCA1040 expected
        Guard a = f()[0];                                                    // E02
        Guard b = h.Get()[0];                                                // E03 SCA1030
        var lam = [return: Borrowed] () => h._l;                             // E04 lambda with mark, natural type Func<>
        Guard c = lam()[0];                                                  // E05
        Func<List<Guard>> lam2 = [return: Borrowed] () => h._l;              // E06
        Action<object> sink = H4.Sink;                                       // E07 fine
        Action<List<Guard>> typed = sink;                                    // E08 contravariance: object receives the list
        typed(h._l);                                                         // E09
        Func<object> make = (Func<List<Guard>>)h.Make;                       // E10 covariance of a delegate value
        Func<List<Guard>> mk = h.Make;
        Func<object> make2 = mk;                                             // E11

        bool held = _g.HeldX;                                                // E31 extension block by-value receiver on a stored guard
        _g.TouchX();                                                         // E32
        _g.TouchByValue();                                                   // E33
        bool held2 = Owner.Enter().HeldX;                                    // E34 fine
        bool held3 = _rg.IsHeld;                                             // E35 readonly field, non-readonly getter? IsHeld is auto readonly? (expression-bodied, not readonly)

        if (_g is IDisposable d) { }                                         // E40 SCA1004
        if (_g is object o) { }                                              // E41
        switch (_g) { case IDisposable d2: break; }                          // E42

        if (_g is IDisposable { } d4) { }                                    // E44
        if (_g is IDisposable) { }                                           // E45 bare type test, fine
        if (maybe is IDisposable d5) { }                                     // E46 boxes a copy out of a stored nullable
        if (_g is var v) { }                                                 // E47 copy
        object? bx = maybe;                                                  // E48 box of a stored nullable
        string s = $"{_g}";                                                  // E49 interpolation boxes/copies?
        string s2 = _g.ToString();                                           // E50 ValueType.ToString boxes
    }

    static void G<[NonCopyable] T>(ref T value, in T inValue) where T : IDisposable
    {
        if (value is IDisposable d) { }                                      // E60 boxes a stored T
        object o = value;                                                    // E61 SCA1004
        inValue.Dispose();                                                   // E62 copy (in)
        value.Dispose();                                                     // E63 fine
        string s = value.ToString()!;                                        // E64
    }
}
