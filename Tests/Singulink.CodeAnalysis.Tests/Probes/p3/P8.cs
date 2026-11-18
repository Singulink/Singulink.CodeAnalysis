using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

class Guards8 : List<Guard> { }
interface IGuards8 : IReadOnlyList<Guard> { }
class GuardTask8 : Task<Guard> { public GuardTask8() : base(() => default) { } }
class MyList8<T> : List<T> { }
class Base8<T> : List<T> { }
class Wrapper8 : Base8<Task<Guard>> { }

interface IMove8 { T Take<[NonCopyable] T>(ref T x); }
class B8 : IMove8 { public T Take<[NonCopyable] T>(ref T x) => NonCopyable.Move(ref x); }
class Mid8 : B8 { public new T Take<T>(ref T x) => x; }                             // (a member that hides)
class D8 : Mid8, IMove8 { }                                                         // I30 SCA1040 on D8
struct S8 : IMove8 { public T Take<T>(ref T x) => x; }                              // I31 SCA1040
abstract class A8 : IMove8 { public abstract T Take<T>(ref T x); }                  // I32 SCA1040
interface IMove8g<U> { T Take<[NonCopyable] T>(ref T x, U u); }
class G8 : IMove8g<int> { public T Take<T>(ref T x, int u) => x; }                   // I33 SCA1040

struct PrimS8(Guard g) { public void Use() => g.Touch(); }                          // I40 SCA1010
record struct RecS8(Guard G);                                                       // I41 SCA1010/1011
class C8 { public Guard F; public C8 Clone() => (C8)base.MemberwiseClone(); }       // I42 SCA1005

static class Mv8
{
    static void Noop<[NonCopyable] T>(ref T v) { }
    static void Assign<[NonCopyable] T>([MovedFrom] ref T v) => v = default!;
    public static T M1<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; Noop(ref v); return a; }          // I50 SCA1041
    public static T M2<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; Assign(ref v); return a; }        // I51 fine
    public static T M3<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; (v, _) = (default(T)!, 0); return a; }   // I52 (deconstruction assigns)
    public static T M4<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; ref T r = ref v; r = default!; return a; } // I53 (assigns through a ref local)
    public static T M5<[NonCopyable] T>([MovedFrom] ref T v) { T a = v; v = default!; T b = v; return a; }   // I54 second read
    public static Task<Guard>? M6([MovedFrom] ref Task<Guard>? v) { var a = v; Interlocked.Exchange(ref v, null); return a; }  // I55
}

class P8
{
    private Task<Guard>? _a, _b;
    private Stack<Task<Guard>> _s1 = new(), _s2 = new();
    private Queue<List<Guard>> _q = new();
    private Action? _act;

    async Task M(Guards8 guards, IGuards8 ig, GuardTask8 gt, MyList8<Guard> ml, Wrapper8 w, List<Guard> list)
    {
        _a = Owner.EnterTaskAsync(); _b = _a;
        Guard g1 = await Interlocked.Exchange(ref _a, null)!;                       // I01 SCA1030
        Guard g2 = await Interlocked.Exchange(ref _b, null)!;                       // I02 SCA1030
        var t = Owner.EnterTaskAsync();
        _s1.Push(t); _s2.Push(t);
        Guard g3 = await _s1.Pop();                                                 // I03 SCA1030
        _q.Enqueue(list);
        Guard g4 = _q.Dequeue()[0];                                                 // I04 SCA1030
        _s1.TryPop(out var popped);
        Guard g5 = await popped!;                                                   // I05 SCA1030

        Guard a = guards[0];                                                        // I10 SCA1030
        IList il = guards;                                                          // I11 SCA1022
        object o = guards;                                                          // I12 SCA1023
        Guard b = ig[0];                                                            // I13 SCA1030
        Guard c = gt.Result;                                                        // I14 SCA1030
        Guard d = ml[0];                                                            // I15 SCA1030
        Guard e = await w[0];                                                       // I16 SCA1030
        object o2 = w;                                                              // I17 SCA1023
        Task tt = gt;                                                               // I18 erasable? GuardTask8 derives from Task<Guard>
        foreach (Guard f in guards) { }                                             // I19 SCA1030
    }

    Guard Captured()
    {
        Guard g = Owner.Enter();
        _act = () => g.Dispose();
        return g;                                                                   // I20 SCA1002
    }

    Guard CapturedLocalFn()
    {
        Guard g = Owner.Enter();
        void L() => g.Dispose();
        _act = L;
        return g;                                                                   // I21 SCA1002
    }

    Guard CapturedParam(Guard g)
    {
        _act = () => g.Dispose();
        return g;                                                                   // I22 SCA1002
    }

    Guard CapturedNotDelegate()
    {
        Guard g = Owner.Enter();
        L();
        return g;                                                                   // I23 (a local function that only runs now)
        void L() => g.Touch();
    }
}
