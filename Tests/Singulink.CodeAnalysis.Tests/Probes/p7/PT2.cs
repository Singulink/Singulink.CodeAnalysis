using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public static class RTPT2
{
    static List<Guard> Make(Lock l) { var x = new List<Guard>(); x.Add(l.Enter()); return x; }
    static Lease MakeLease(Lock l) => new Lease { Inner = l.Enter(), Id = 1 };

    public static Lock L01() { var l = new Lock(); if (Make(l) is [var a] and [var b]) { a.Dispose(); b.Dispose(); } return l; }   // L01
    public static Lock L02()
    {
        var l = new Lock(); var x = MakeLease(l);
        switch (x) { case { Inner: var g } whole: g.Dispose(); whole.Inner.Dispose(); break; }   // L02
        return l;
    }
    static Guard Take([MovedFrom] ref Guard a, out Guard second)   // L03
    {
        if (a is var x and var y) { a = default; second = y; return x; }
        a = default; second = default; return default;
    }
    public static Lock L04() { var l = new Lock(); Guard g = l.Enter(); using Guard a = Take(ref g, out Guard b); b.Dispose(); return l; }   // L04
}
