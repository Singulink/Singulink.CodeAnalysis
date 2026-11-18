using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Sink;

[InlineArray(2)] public struct Buf2<[NonCopyable] T> { private T _e; }

public static class RTLU2
{
    // two variables declared by one pattern from one value
    public static Lock B01() { var l = new Lock(); Guard g = l.Enter(); if (g is var x and var y) { x.Dispose(); y.Dispose(); } return l; }   // B01
    public static Lock B02() { var l = new Lock(); if (l.Enter() is var x and var y) { x.Dispose(); y.Dispose(); } return l; }   // B02 (new value, pre-existing)
    public static Lock B03() { var l = new Lock(); Guard g = l.Enter(); if (g is { } x && g is { } y) { x.Dispose(); y.Dispose(); } return l; }   // B03 reported
    public static Lock B04() { var l = new Lock(); Guard g = l.Enter(); var r = g switch { var x when x.IsHeld => Take2(x, ref g), _ => 0 }; return l; }   // B04
    static int Take2(Guard x, ref Guard g) { x.Dispose(); g.Dispose(); return 0; }
    public static Lock B05() { var l = new Lock(); (Guard, int) t = (l.Enter(), 1); if (t is (var a, _) and (var b, _)) { a.Dispose(); b.Dispose(); } return l; }   // B05
    public static Lock B06() { var l = new Lock(); Guard? m = l.Enter(); if (m is { } x and { } y) { x.Dispose(); y.Dispose(); } return l; }   // B06

    // array local, a ref into it, then a read at its last use
    public static Lock B07()
    {
        var l = new Lock();
        Guard[] arr = new Guard[1]; arr[0] = l.Enter();
        ref Guard r = ref arr[0];
        Guard a = arr[0];                         // B07 must be reported
        r.Dispose(); a.Dispose();
        return l;
    }

    // inline array local
    public static Lock B08()
    {
        var l = new Lock();
        Buf2<Guard> buf = default; buf[0] = l.Enter();
        Span<Guard> s = buf;
        Guard a = buf[0];                         // B08 must be reported
        s[0].Dispose(); a.Dispose();
        return l;
    }
    public static Lock B09()
    {
        var l = new Lock();
        Buf2<Guard> buf = default; buf[0] = l.Enter();
        foreach (Guard x in buf) Consume(x);      // B09 copy of each element
        buf[0].Dispose();
        return l;
    }

    // a value capture read twice
    public static Lock B10(bool c)
    {
        var l = new Lock(); Guard a = l.Enter(), b = l.Enter();
        Held = c ? a : b;                         // B10 clean
        Held.Dispose(); Held = default;
        return l;
    }

    // ?. on a nullable at last use
    public static Lock B11() { var l = new Lock(); Guard? m = l.Enter(); m?.Dispose(); m.Value.Dispose(); return l; }   // B11
    // ??= 
    public static Lock B12() { var l = new Lock(); Guard? m = l.Enter(); Guard? n = m; return l; }   // B12 clean? last use

    // a by-value parameter of a local function, recursion
    public static Lock B13()
    {
        var l = new Lock();
        F(l.Enter(), 2);
        return l;
        static void F(Guard g, int n) { if (n > 0) F(g, n - 1); else g.Dispose(); }   // B13 clean
    }

    // out argument and a read in the same call
    static void Pass(Guard a, out Guard b) => b = a;
    public static Lock B14() { var l = new Lock(); Guard g = l.Enter(); Pass(g, out g); g.Dispose(); return l; }   // B14 clean

    // struct with Guard field: parts
    public static Lock B15()
    {
        var l = new Lock();
        Pair p = new Pair { A = l.Enter(), B = l.Enter() };
        Held = p.A;                               // B15 not the last: reported
        Consume(p.B);                             // B16 last
        Held.Dispose(); Held = default;
        return l;
    }

    // partial write after last read of the whole
    public static Lock B17()
    {
        var l = new Lock(); Pair p = new Pair { A = l.Enter() };
        Held = p.A;                               // B17 p.A then p.A written: is a write of a part a use?
        p.A.Field = 3;
        Held.Dispose(); p.A.Dispose(); Held = default;
        return l;
    }

    // a read in a do/while
    public static Lock B18()
    {
        var l = new Lock(); Guard g = l.Enter(); int i = 0;
        do { Consume(g); } while (++i < 2);       // B18 reported
        return l;
    }

    // continue/break in a while(true)
    public static Lock B19()
    {
        var l = new Lock(); Guard g = l.Enter(); int i = 0;
        while (true) { if (i++ == 0) { Consume(g); continue; } break; }   // B19 reported
        return l;
    }

    // lock statement
    static readonly object s_gate = new();
    public static Lock B20()
    {
        var l = new Lock(); Guard g = l.Enter();
        lock (s_gate) { Held = g; }               // B20 clean (no use after)
        Held.Dispose(); Held = default;
        return l;
    }

    public static Lock B21()
    {
        var l = new Lock(); Guard g = l.Enter();
        try { lock (s_gate) { Held = g; } }       // B21 reported: catch uses g
        catch { g.Dispose(); }
        Held.Dispose(); Held = default;
        return l;
    }

    // await between
    public static async Task<int> B22()
    {
        var l = new Lock(); Guard g = l.Enter();
        await Task.Yield();
        Held = g;                                 // B22 clean
        return 0;
    }

    // yield in an iterator: dispose path into finally
    public static IEnumerable<int> B23(Lock l)
    {
        Guard g = l.Enter();
        try
        {
            yield return 1;
            Held = g;                             // B23 reported (finally)
            g = default;
        }
        finally { g.Dispose(); }
    }

    // tuple deconstruction swap
    public static Lock B24()
    {
        var l = new Lock(); Guard a = l.Enter(), b = l.Enter();
        (a, b) = (b, a);                          // B24 clean
        a.Dispose(); b.Dispose();
        return l;
    }
}

public struct Pair { public Guard A; public Guard B; }
