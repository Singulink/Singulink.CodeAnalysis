using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Sink;

public static class LU1
{
    static Task<Guard> s_task = null!;
    static int Rel(ref Guard g) { g.Dispose(); return 0; }
    static void Two(int a, Guard b) => b.Dispose();
    static void TwoR(Guard b, int a) => b.Dispose();
    sealed class C2 { public C2(int a, Guard b) => b.Dispose(); public C2() {} public int this[int a, Guard b] { get { b.Dispose(); return 0; } } }

    // named arguments out of order: g read first, then Release(ref g)
    public static Lock A01() { var l = new Lock(); Guard g = l.Enter(); Two(b: g, a: Rel(ref g)); return l; }   // A01 must be reported
    public static Lock A02() { var l = new Lock(); Guard g = l.Enter(); _ = new C2(b: g, a: Rel(ref g)); return l; }   // A02 must be reported
    public static Lock A03() { var l = new Lock(); Guard g = l.Enter(); _ = new C2()[b: g, a: Rel(ref g)]; return l; }   // A03 must be reported
    public static Lock A04() { var l = new Lock(); Guard g = l.Enter(); TwoR(g, Rel(ref g)); return l; }   // A04 must be reported (positional)

    // using (g) with a local: the hidden copy is disposed
    public static Lock A05() { var l = new Lock(); Guard g = l.Enter(); using (g) { Consume(g); } return l; }   // A05 must be reported (one of the two lines)

    // a struct wrapper over a shared task
    public static void A06()
    {
        var vt1 = new ValueTask<Guard>(s_task);   // A06 creation from a stored task
        var vt2 = new ValueTask<Guard>(s_task);   // A07
        using Guard a = vt1.Result;               // A08
        using Guard b = vt2.Result;               // A09
    }

    public static void A10(List<Guard> list)
    {
        var e = list.GetEnumerator();             // A10 enumerator of a parameter list
        e.MoveNext();
        Guard a = e.Current;                      // A11 last use of e
        Guard b = list[0];                        // A12
    }

    public static Lock A13()
    {
        var l = new Lock();
        var list = new List<Guard>(); list.Add(l.Enter());
        var e = list.GetEnumerator();             // A13 not the last use of list
        e.MoveNext();
        Guard a = e.Current;                      // A14 last use of e
        Guard b = list[0];                        // A15 last use of list
        a.Dispose(); b.Dispose();
        return l;
    }

    public static Lock A16()
    {
        var l = new Lock();
        var list = new List<Guard>(); list.Add(l.Enter());
        var e = list.GetEnumerator();             // A16
        Guard b = list[0];                        // A17 last use of list
        e.MoveNext();
        Guard a = e.Current;                      // A18 last use of e
        a.Dispose(); b.Dispose();
        return l;
    }

    // a deconstructed tuple of a container read out of a value at its last use
    public static async Task A19()
    {
        (Task<Guard> T, int N) pair = (s_task, 1);   // A19
        using Guard a = await pair.T;                // A20 container out of a value: stored
    }

    // goto loop
    public static Lock A21()
    {
        var l = new Lock(); Guard g = l.Enter(); int i = 0;
    again:
        Consume(g);                               // A21 used again by the goto: reported
        if (++i < 2) goto again;
        return l;
    }

    // switch with fallthrough by goto case
    public static Lock A22(int k)
    {
        var l = new Lock(); Guard g = l.Enter();
        switch (k)
        {
            case 0: Consume(g); goto case 1;      // A22 reported
            case 1: g.Dispose(); break;
        }
        return l;
    }

    // short-circuit: the read on the left, the use on the right
    static bool Take(Guard g) { g.Dispose(); return true; }
    static bool Use(ref Guard g) { g.Dispose(); return true; }
    public static Lock A23() { var l = new Lock(); Guard g = l.Enter(); bool r = Take(g) && Use(ref g); return l; }   // A23 reported
    public static Lock A24() { var l = new Lock(); Guard g = l.Enter(); bool r = Take(g) || Use(ref g); return l; }   // A24 reported (not taken at run time)
    public static Lock A25(bool c) { var l = new Lock(); Guard g = l.Enter(); bool r = c ? Take(g) : Use(ref g); return l; }   // A25 clean

    // conditional ref and assignment through it
    public static Lock A26(bool c)
    {
        var l = new Lock(); Guard a = l.Enter(), b = l.Enter();
        Held = a;                                  // A26 a is assigned through the ref below only maybe: reported
        (c ? ref a : ref b) = l.Enter();
        a.Dispose(); b.Dispose();
        return l;
    }

    // switch expression
    public static Lock A27(int k)
    {
        var l = new Lock(); Guard g = l.Enter();
        int r = k switch { 0 => Take(g) ? 1 : 0, _ => 2 };    // A27 clean
        return l;
    }

    // throw expression: an exception path to a catch that uses g
    public static Lock A28(object? o)
    {
        var l = new Lock(); Guard g = l.Enter();
        try { Held = g; var x = o ?? throw new Exception(); }  // A28 reported (catch uses g)
        catch { g.Dispose(); }
        return l;
    }

    // nested try finally
    public static Lock A29()
    {
        var l = new Lock(); Guard g = l.Enter();
        try { try { Held = g; } finally { } }      // A29 reported
        finally { g.Dispose(); }
        return l;
    }

    // filter
    static bool F(ref Guard g) { g.Dispose(); return false; }
    public static Lock A30()
    {
        var l = new Lock(); Guard g = l.Enter();
        try { Held = g; throw new Exception(); }   // A30 reported
        catch when (F(ref g)) { }
        catch { }
        return l;
    }
}
