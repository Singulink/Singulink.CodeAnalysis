using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Singulink.CodeAnalysis;

public static class RT3
{
    static Memory<Guard> s_mem;
    static ArraySegment<Guard> s_seg;
    static ImmutableArray<Guard> s_imm;

    static Guard[] ViaImplicit(Lock l) { var a = new Guard[1]; a[0] = l.Enter(); s_mem = a; return a; }                     // Y01
    static Guard[] ViaSegment(Lock l) { var a = new Guard[1]; a[0] = l.Enter(); s_seg = a; return a; }                     // Y02
    static Guard[] ViaImm(Lock l) { var a = new Guard[1]; a[0] = l.Enter(); s_imm = ImmutableCollectionsMarshal.AsImmutableArray(a); return a; }   // Y03
    static Memory<Guard> Window(Guard[] a) => a;
    static Guard[] ViaHelper(Lock l) { var a = new Guard[1]; a[0] = l.Enter(); s_mem = Window(a); return a; }             // Y04
    static List<Guard> ViaSpan(Lock l) { var a = new List<Guard>(); a.Add(l.Enter()); return a; }

    public static Lock Y01() { var l = new Lock(); IReadOnlyList<Guard> r = ViaImplicit(l); Guard g = r[0]; g.Dispose(); s_mem.Span[0].Dispose(); return l; }   // Y05
    public static Lock Y02() { var l = new Lock(); IEnumerable<Guard> r = ViaSegment(l); foreach (Guard g in r) g.Dispose(); s_seg.AsSpan()[0].Dispose(); return l; } // Y06
    public static Lock Y03() { var l = new Lock(); IList<Guard> r = ViaImm(l); Guard g = r[0]; g.Dispose(); ImmutableCollectionsMarshal.AsArray(s_imm)![0].Dispose(); return l; }  // Y07
    public static Lock Y04() { var l = new Lock(); IList<Guard> r = ViaHelper(l); Guard g = r[0]; g.Dispose(); s_mem.Span[0].Dispose(); return l; }   // Y08

    // in one method
    public static Lock Y09()
    {
        var l = new Lock();
        var a = new Guard[1];
        a[0] = l.Enter();
        Memory<Guard> m = a.AsMemory();
        IList<Guard> il = a;                                            // Y09
        Guard g = il[0];                                                // Y10
        g.Dispose();
        m.Span[0].Dispose();
        return l;
    }

    // a plain cast of a returned array, no field: the array element still is a variable of the caller's
    public static Lock Y11()
    {
        var l = new Lock();
        Guard[] a = ViaImplicit(l);
        Guard g = ((IList<Guard>)a)[0];                                 // Y11
        g.Dispose();
        a[0].Dispose();                                                 // Y12
        return l;
    }
}
