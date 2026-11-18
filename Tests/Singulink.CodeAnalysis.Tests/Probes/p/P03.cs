using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

// ---------- records, with, clone
record class R1(Guard G);                                        // D01 report
record struct R2(Guard G);                                       // D02 report
[NonCopyable] record struct R3(Guard G);                         // D03 report
record class R4(int X) { public Guard F; }                       // D04 report
record class R5(Guard g) { public void Use() => g.Touch(); }     // D05 report? captured param in record (not property... it is property)
class R6 { public Guard F; public R6 Clone() => (R6)MemberwiseClone(); } // D06 report
class R7(Guard g) { public void Use() => g.Touch(); public R7 Clone() => (R7)MemberwiseClone(); } // D07 report? hidden field
class R8Base { public Guard F; }
class R8 : R8Base { public R8 Clone() => (R8)MemberwiseClone(); } // D08 report
class R9<[NonCopyable] T> { public T F = default!; public R9<T> Clone() => (R9<T>)MemberwiseClone(); } // D09 report
class R10 { public Guard? F; public R10 Clone() => (R10)base.MemberwiseClone(); } // D10 report
class R11 { public (Guard, int) F; public object C() => MemberwiseClone(); }      // D11 report
class R12 { public List<Guard> F = new(); public object C() => MemberwiseClone(); } // D12 OK
class R13 { public Guard F; public object C(R13 other) => other.MemberwiseClone(); } // D13 report (CS1540? protected access ok same type)

class WithUse
{
    void M(R2 stored)
    {
        var a = stored with { };                                 // D20 report
        var b = new R2(Enter()) with { };                        // D21 OK
        (Guard, int) t = (Enter(), 1);
        var c = t with { Item2 = 2 };                            // D22 report
        var d = (Enter(), 1) with { Item2 = 3 };                 // D23 OK
        var e = MakeKvp() with { };                              // D24 OK
    }
    static KeyValuePair<int, Guard> MakeKvp() => default;
}

struct PrimS2(Guard g)                                           // D30 report: hidden field in copyable struct
{
    public void Use() => g.Touch();
}

class PrimHolder
{
    void M()
    {
        var a = new PrimS2(Enter());
        var b = a;                                               // D31 copies the guard inside
        a.Use(); b.Use();
    }
}

// ---------- marks through interfaces
interface IMove { T Take<[NonCopyable] T>(ref T x); }
class Base1 : IMove { public T Take<[NonCopyable] T>(ref T x) => NonCopyable.Move(ref x); }
class Derived1 : Base1, IMove { T IMove.Take<T>(ref T x) => x; }          // E01 report: re-implementation drops mark
class Derived2 : Base1, IMove { public new T Take<T>(ref T x) => x; }     // E02 report
interface IMove2 { T Take<[NonCopyable] T>(ref T x); }
class Impl2 : IMove2 { public T Take<T>(ref T x) => x; }                  // E03 report
class Base3 { public T Take<T>(ref T x) => x; }
class Derived3 : Base3, IMove2 { }                                        // E04 report
interface IDefault { T Take<[NonCopyable] T>(ref T x) => NonCopyable.Move(ref x); }
interface IDerivedDefault : IDefault { T IDefault.Take<T>(ref T x) => x; } // E05 report: DIM re-impl
abstract class AbsBase { public abstract T Take<[NonCopyable] T>(ref T x); }
class AbsDer : AbsBase { public override T Take<T>(ref T x) => x; }      // E06 report
interface IGen<[NonCopyable] T> { T Get(); }
class GenImpl<U> : IGen<U> { public U Get() => default!; }                // E07 report
class GenOuter<[NonCopyable] T> { public class Inner : IGen<T> { public T Get() => default!; } } // E08 OK
interface IStat { static abstract T Make<[NonCopyable] T>(ref T x); }
class StatImpl : IStat { public static T Make<T>(ref T x) => x; }          // E09 report

// ---------- MovedFrom
static class Mv
{
    public static T M1<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = default!; return a; }        // F01 OK
    public static T M2<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; T b = v; v = default!; return a; } // F02 report
    public static T M3<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; if (a is null) v = default!; return a; } // F03 (spec: OK)
    public static T M4<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; Noop(ref v); return a; }          // F04 hole? passes by ref to no-op
    static void Noop<[NonCopyable] T>(ref T v) { }
    public static T M5<[NonCopyable] T>([MovedFrom] ref T v) { v = default!; return v; }                  // F05 OK (returns default)
    public static (T, T) M6<[NonCopyable] T>([MovedFrom] ref T v) { var r = (v, v); v = default!; return r; } // F06 report
    public static T M7<[NonCopyable] T>([MovedFrom] ref T v) { ref T r = ref v; T a = r; v = default!; T b = r; return a; } // F07 report b
    public static T M8<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = default!; Func<T> f = () => a; return a; } // F08 report (local captured & returned)
    public static T M9<[NonCopyable] T>([MovedFrom] ref T v, bool c) { v = default!; return c ? v : v; } // F09 report? two syntactic reads, one path
    public static T M10<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = default!; return v is { } ? a : a; } // F10 is-pattern read?
    public static void M11<[NonCopyable] T>([MovedFrom] ref T v, out T o) { o = v; v = default!; }      // F11 OK
    public static T M12<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = default!; goto L; L: return a; } // F12 OK
    public static T M13<[NonCopyable] T>([MovedFrom] ref T v) { T a = v!; v = default!; return a; }      // F13 OK
    public static T M14<[NonCopyable] T>([MovedFrom] ref T v) { T a = (v); v = default!; return a; }     // F14 OK
    public static T M15<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = a; return Mv.M1(ref v); }  // F15 report: a and v
    public static T M16<[NonCopyable] T>([MovedFrom] ref T v) { T a = default!; a = v; v = default!; return a; } // F16 OK
    public static T M17<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = default!; var t = (a, 1); return a; } // F17 report a in tuple
    public static T M18<[NonCopyable] T>([MovedFrom] ref T v) { (T a, int b) = (v, 1); v = default!; return a; } // F18 OK
    public static T M19<[NonCopyable] T>([MovedFrom] ref T v) { (T a, T b) = (v, v); v = default!; return a; } // F19 report
    public static void M20<[NonCopyable] T>([MovedFrom] ref T v, T[] arr) { arr[0] = v; v = default!; } // F20 OK
    public static void M21<[NonCopyable] T>([MovedFrom] ref T v, T[] arr) { arr[0] = v; arr[1] = v; v = default!; } // F21 report
    public static void M22<[NonCopyable] T>([MovedFrom] ref T v, T[] arr) { arr[0] = v; v = default!; arr[1] = v; } // F22 report? reads default
    public static async Task<int> M23<[NonCopyable] T>(T[] arr) { await Task.Yield(); return 1; }
    public static T M24<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = default!; return Swap(ref a, ref v); } // F24
    static T Swap<[NonCopyable] T>(ref T a, ref T b) => NonCopyable.Move(ref a);
    public static T M25<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v ??= default!; return a; }      // F25 report not assigned
    public static T M26<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; (v, _) = (default(T)!, 1); return a; } // F26 OK
}
