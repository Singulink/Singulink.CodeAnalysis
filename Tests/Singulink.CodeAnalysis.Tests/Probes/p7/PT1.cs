using System;
using Singulink.CodeAnalysis;

[NonCopyable] public struct Lease { public Guard Inner; public int Id; }

public static class RTPT1
{
    static Lease MakeLease(Lock l) => new Lease { Inner = l.Enter(), Id = 1 };
    static (Guard, int) MakeTuple(Lock l) => (l.Enter(), 1);

    // the whole and a part, from one value at its last use
    public static Lock I01() { var l = new Lock(); Lease x = MakeLease(l); if (x is { Inner: var g } whole) { g.Dispose(); whole.Inner.Dispose(); } return l; }   // I01
    // the same from a call
    public static Lock I02() { var l = new Lock(); if (MakeLease(l) is { Inner: var g } whole) { g.Dispose(); whole.Inner.Dispose(); } return l; }   // I02
    // positional on a tuple
    public static Lock I03() { var l = new Lock(); if (MakeTuple(l) is (var g, _) whole) { g.Dispose(); whole.Item1.Dispose(); } return l; }   // I03
    // switch expression arm
    public static Lock I04() { var l = new Lock(); var x = MakeLease(l); int r = x switch { { Inner: var g } whole => Both(g, whole), }; return l; }   // I04
    static int Both(Guard g, Lease w) { g.Dispose(); w.Inner.Dispose(); return 0; }
    // 'or' cannot declare; 'and' with a part on each side
    public static Lock I05() { var l = new Lock(); if (MakeLease(l) is { Inner: var g } and { Inner: var h }) { g.Dispose(); h.Dispose(); } return l; }   // I05
}
