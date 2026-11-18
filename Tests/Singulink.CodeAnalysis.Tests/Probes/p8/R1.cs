using System;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable] public struct Lease { public Guard Inner; public int Id; public Guard Other; }
[NonCopyable] public struct Pair2 { public Guard A; public Guard B;
    public void Deconstruct(out Guard a, out Guard b) { a = NonCopyable.Move(ref A); b = NonCopyable.Move(ref B); } }
public sealed class RefHolder { public Guard G; public ref Guard GRef => ref G; }

public static class R1
{
    public static Guard Held;
    public static Guard Prop { get => default; set { } }
    static void Work() { }
    static bool Log(in Guard g) => false;
    static Lease MakeLease(Lock l) => new Lease { Inner = l.Enter() };

    // ---- patterns: one variable for one value
    public static void P01(Lock l) { Lease x = MakeLease(l); if (x is var whole and { Inner: var g }) { } }       // A01 reversed order
    public static void P02(Lock l) { Lease x = MakeLease(l); if (x is { Inner: var g, Inner: var h }) { } }      // A02 same member twice
    public static void P03(Lock l) { Lease x = MakeLease(l); switch (x) { case { Inner: var g } whole: break; } } // A03 case label
    public static void P04(Lock l) { var r = MakeLease(l) switch { { Inner: var g } w => 1 }; }           // A04 switch arm
    public static void P05(Lock l) { var t = (l.Enter(), 1); if (t is (var g, _) whole) { } }                     // A05 tuple
    public static void P06(Lock l) { Guard[] a = new Guard[2]; if (a is [var x, ..] and [_, var y]) { } }         // A06 two from start, different elems
    public static void P07(Lock l) { Guard[] a = new Guard[2]; if (a is [var x, ..] and [.., var y]) { } }        // A07 start + end
    public static void P08(Lock l) { Guard[] a = new Guard[2]; if (a is [var x, ..] and [var y, ..]) { } }        // A08 same elem from start
    public static void P09(RefHolder h) { if (h is { GRef: var a }) { } }                                          // A09 read through ref property
    public static void P10(RefHolder h) { if (h is { G: var a }) { } }                                             // A10 field of a class
    public static void P11(Lock l) { Lease x = MakeLease(l); if (x is { Inner: var g, Other: var o }) { } }       // A11 two parts: OK
    public static void P12(Lock l) { Lease x = MakeLease(l); if (x is { Inner: { } g } and { Inner.IsHeld: true }) { } } // A12 ok-ish
    public static void P13(Lock l) { var t = (l.Enter(), l.Enter()); if (t is (var a, var b) and (_, var c)) { } } // A13 overlapping second
    public static void P14(Lock l) { Guard[] a = new Guard[3]; if (a is [var x, .. var rest]) { } }              // A14 slice of array
    public static void P15(Lock l) { Lease x = MakeLease(l); if (x is { Inner: var g } and var w2) { } }          // A15

    // ---- filters
    public static void F01(Lock l) { Guard g = l.Enter(); try { Work(); } catch (InvalidOperationException) when (Log(g)) { } catch (Exception) { g.Dispose(); } }          // B01
    public static void F02(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch when (Log(g)) { } } catch { g.Dispose(); } }   // B02 outer catch
    public static void F03(Lock l) { Guard g = l.Enter(); try { Work(); } catch when (Use(g)) { } finally { } }                       // B03 filter takes it, nothing after
    public static void F04(Lock l) { Guard g = l.Enter(); try { Work(); } catch when (Use(g)) { } g.Dispose(); }                     // B04 used after the try
    static bool Use(Guard g) { g.Dispose(); return false; }

    // ---- commit at end of try
    public static void C01(Lock l, RefHolder h) { Guard g = l.Enter(); try { Work(); h.G = g; } catch { g.Dispose(); throw; } }     // C01 field of object: clean
    public static void C02(Lock l, Guard[] a) { Guard g = l.Enter(); try { Work(); a[0] = g; } catch { g.Dispose(); throw; } }      // C02 element: clean
    public static void C03(Lock l) { Guard g = l.Enter(); try { Work(); try { Held = g; } finally { } } catch { g.Dispose(); throw; } }  // C03 nested try w/ finally
    public static void C04(Lock l) { Guard g = l.Enter(); try { Work(); try { Held = g; } catch { } Work(); } catch { g.Dispose(); throw; } } // C04 inner then Work: reported
    public static void C05(Lock l, bool c) { Guard g = l.Enter(); try { if (c) { Held = g; } else { Work(); } Work(); } catch { g.Dispose(); throw; } } // C05 store not last block: reported
    public static void C06(Lock l, bool c) { Guard g = l.Enter(); try { Work(); if (c) { Held = g; return; } Held = g; } catch { g.Dispose(); throw; } } // C06 two stores, each at end
    public static void C07(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; g = default; } finally { g.Dispose(); } }           // C07 implementer call: clean
    public static void C08(Lock l) { Guard g = l.Enter(); try { Work(); Prop = (Held = g); } catch { g.Dispose(); throw; } }      // C08 setter after the store
    public static void C09(Lock l, RefHolder h) { Guard g = l.Enter(); try { Work(); h.GRef = g; } catch { g.Dispose(); throw; } } // C09 store through ref-returning prop
    public static void C10(Lock l, RefHolder[] hs, int i) { Guard g = l.Enter(); try { Work(); hs[i].G = g; } catch { g.Dispose(); throw; } } // C10
    public static void C11(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; int z = 0; z = 10 / z; } catch { g.Dispose(); throw; } } // C11 division after
    public static void C12(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; } catch { g.Dispose(); throw; } finally { Work(); } } // C12 catch + finally w/o use
    public static void C13(Lock l, object o) { Guard g = l.Enter(); try { Work(); Held = g; _ = (string)o; } catch { g.Dispose(); throw; } } // C13 cast after
    public static void C14(Lock l, Guard[] a) { Guard g = l.Enter(); try { Work(); a[a.Length] = g; } catch { g.Dispose(); throw; } } // C14 store throws (OK)
    public static void C15(Lock l) { Guard g = l.Enter(); try { Work(); lock (l) { Held = g; } } catch { g.Dispose(); throw; } } // C15 inside lock: reported
    public static async Task C16(Lock l) { Guard g = l.Enter(); try { await Task.Yield(); Held = g; } catch { g.Dispose(); throw; } } // C16 async: clean

    // ---- MovedFrom relatives
    static Guard M01([MovedFrom] ref Guard a, ref Guard old, int d) { old = a; int q = 10 / d; a = default; return default; }  // D01 division by zero between
    static Guard M02([MovedFrom] ref Guard a, ref Guard old, int x) { old = a; int q = checked(x + int.MaxValue); a = default; return default; } // D02 overflow
    static Guard M03([MovedFrom] ref Guard a, ref Guard old, object o) { old = a; string s = $"{o}"; a = default; return default; } // D03 interpolation
    static Task<Guard> M04([MovedFrom] ref Task<Guard> t) { var m = t; _ = Interlocked.Exchange(ref t, null!); return m; }      // D04 discard
    static Task<Guard> M05([MovedFrom] ref Task<Guard> t) { var m = t; var n = Interlocked.CompareExchange(ref t, null!, m); return m; } // D05 CompareExchange
    static Guard M06([MovedFrom] ref Guard a, ref Guard old, object o) { old = a; var t = ((object)1, o); a = default; return default; } // D06 tuple
    static Guard M07([MovedFrom] ref Guard a, ref Guard old, int[] arr) { old = a; int n = arr.Length; a = default; return default; } // D07 length: reported
    static Guard M08([MovedFrom] ref Guard a, ref Guard old, long v) { old = a; int n = checked((int)v); a = default; return default; } // D08 checked cast
    static Guard M09([MovedFrom] ref Guard a, ref Guard old, object o) { old = a; bool b = o is int; a = default; return default; } // D09 type test (fine)
    static Guard M10([MovedFrom] ref Guard a, ref Guard old, object o) { old = a; int b = (int)o; a = default; return default; }  // D10 unbox can throw
    static Guard M11([MovedFrom] ref Guard a, ref Guard old, int d) { old = a; int q = 10 % d; a = default; return default; }  // D11 modulo
    static Guard M12([MovedFrom] ref Guard a, ref Guard old, string s) { old = a; bool b = s == "x"; a = default; return default; } // D12 string ==
    static Guard M13([MovedFrom] ref Guard a, ref Guard old, decimal d) { old = a; decimal q = 1m / d; a = default; return default; } // D13 decimal op
    static Guard M14([MovedFrom] ref Guard a, ref Guard old) { old = a; a = default; return default; }  // D14 baseline OK

    // ---- deconstruct
    public static void E01(Lock l) { Pair2 p = new Pair2 { A = l.Enter(), B = l.Enter() }; var (x, y) = p; x.Dispose(); y.Dispose(); } // E01 clean
    public static void E02(Lock l) { Pair2 p = new Pair2 { A = l.Enter(), B = l.Enter() }; var (x, y) = p; p.A.Dispose(); }          // E02 p used after: reported
    public static void E03(Lock l) { Pair2 p = new Pair2 { A = l.Enter(), B = l.Enter() }; Held = p.A; var (x, y) = p; }            // E03 copy before

    // ---- runtime
    public static Lock RunD01() { var l = new Lock(); Guard a = l.Enter(); Guard old = default; try { M01(ref a, ref old, 0); } catch { } a.Dispose(); old.Dispose(); return l; }
    public static Lock RunD02() { var l = new Lock(); Guard a = l.Enter(); Guard old = default; try { M02(ref a, ref old, 5); } catch { } a.Dispose(); old.Dispose(); return l; }
    public static Lock RunD10() { var l = new Lock(); Guard a = l.Enter(); Guard old = default; try { M10(ref a, ref old, "x"); } catch { } a.Dispose(); old.Dispose(); return l; }
}
public static class RT1
{
    public static Lock D01() => R1.RunD01();
    public static Lock D02() => R1.RunD02();
    public static Lock D10() => R1.RunD10();
}
