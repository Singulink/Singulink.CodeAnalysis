using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public ref struct RefG { public ref Guard G; public RefG(ref Guard g) { G = ref g; } }

public static class H1
{
    public static Guard Held, Held2;
    static void Work() { }
    static bool Use(Guard g) { g.Dispose(); return false; }

    // anonymous types
    public static void A01(Lock l) { var a = new { G = l.Enter() }; Guard x = a.G; Guard y = a.G; }     // A01 anon property read twice
    public static void A02(Lock l) { var a = new { G = l.Enter(), N = 1 }; var b = a with { N = 2 }; } // A02 anon with

    // container locals at last use with references taken
    public static void B01(Lock l) { var arr = new Guard[1]; arr[0] = l.Enter(); ref Guard r = ref arr[0]; Guard x = arr[0]; r.Dispose(); x.Dispose(); } // B01 ref into array then read
    public static void B02(Lock l) { var list = new List<Guard>(); list.Add(l.Enter()); ref Guard r = ref CollectionsMarshal.AsSpan(list)[0]; Guard x = list[0]; r.Dispose(); x.Dispose(); } // B02
    public static void B03(Lock l) { var list = new List<Guard>(); list.Add(l.Enter()); var alias = list; Guard x = list[0]; alias[0].Dispose(); x.Dispose(); } // B03
    public static void B04(Lock l) { var list = new List<Guard>(); list.Add(l.Enter()); Action a = () => list.Clear(); Guard x = list[0]; } // B04
    public static void B05(Lock l) { var arr = new Guard[1]; arr[0] = l.Enter(); Span<Guard> s = arr; Guard x = arr[0]; s[0].Dispose(); x.Dispose(); } // B05
    public static void B06(Lock l) { var arr = new Guard[1]; arr[0] = l.Enter(); ref Guard r = ref MemoryMarshal.GetArrayDataReference(arr); Guard x = arr[0]; r.Dispose(); x.Dispose(); } // B06
    public static void B07(Lock l) { var list = new List<Guard>(); list.Add(l.Enter()); foreach (var g in list) { g.Dispose(); } } // B07 own list foreach last use: clean
    public static void B08(Lock l) { var arr = new Guard[1]; arr[0] = l.Enter(); Sink.Touch(ref arr[0]); Guard x = arr[0]; } // B08
    public static void B09(Lock l) { var t = l.EnterTaskAsync(); var t2 = Volatile.Read(ref t); var a = t2.Result; var b = t.Result; } // B09
    public static void B10(Lock l) { var arr = new Guard[1]; arr[0] = l.Enter(); var seg = new ArraySegment<Guard>(arr); Guard x = arr[0]; } // B10

    // switch when
    public static void S01(Lock l, int n) { Guard g = l.Enter(); switch (n) { case 1 when Use(g): break; case 2: g.Dispose(); break; } } // S01

    // refs
    public static void R01(Lock l) { Guard g = l.Enter(); var r = new RefG(ref g); Held = g; r.G.Dispose(); }                 // R01
    public static void R02(Lock l) { Guard g = l.Enter(); RefG r = new RefG(ref g); Held = g; r.G.Dispose(); }            // R02
    public static void R03(Lock l, bool c) { Guard g = l.Enter(); Guard h = l.Enter(); ref Guard r = ref (c ? ref g : ref h); Held = g; r.Dispose(); } // R03
    public static void R04(Lock l) { Guard g = l.Enter(); var s = new ReadOnlySpan<Guard>(in g); Held = g; s[0].Peek(); }      // R04
    public static void R05(Lock l) { Guard g = l.Enter(); ref Guard r = ref Unsafe.AsRef(in g); Held = g; r.Dispose(); }       // R05
    public static void R06(Lock l) { Guard g = l.Enter(); Span<Guard> s = new Span<Guard>(ref g); Held = g; s[0].Dispose(); }  // R06
    public static void R07(Lock l) { Guard g = l.Enter(); scoped ref Guard r = ref g; Held = g; r.Dispose(); }                 // R07
    public static void R08(Lock l) { Guard g = l.Enter(); var s = MemoryMarshal.CreateSpan(ref g, 1); Held = g; s[0].Dispose(); } // R08
    public static void R09(Lock l) { Guard g = l.Enter(); foreach (ref Guard e in new Span<Guard>(ref g)) { Held = g; e.Dispose(); } } // R09
    public static void R10(Lock l) { Guard g = l.Enter(); ref Guard r = ref Pick(ref g); Held = g; r.Dispose(); }               // R10
    static ref Guard Pick(ref Guard g) => ref g;
    public static void R11(Lock l) { Guard g = l.Enter(); TypedReference tr = __makeref(g); Held = g; __refvalue(tr, Guard).Dispose(); } // R11

    // primary constructor
    public sealed class PC(Guard g) { public Guard F = g; public void Close() => g.Dispose(); }  // PC1
    public sealed class PC2(Guard g) { public Guard F = g; }                                   // PC2 clean

    // lambdas, local functions
    public static void L01(Lock l) { Guard g = l.Enter(); Held = g; Action a = () => g.Dispose(); a(); }   // L01 captured later
    public static void L02(Lock l) { Guard g = l.Enter(); Held = g; Local(); void Local() => g.Dispose(); } // L02
    public static void L03(Lock l) { Guard g = l.Enter(); Func<Guard, Guard> f = x => x; Held = f(g); } // L03 clean
    public static Func<Guard> L04(Lock l) { Guard g = l.Enter(); return () => NonCopyable.Move(ref g); } // L04 clean
    public static void L05(Lock l) { Guard g = l.Enter(); Held = g; Action a = static () => { }; Expr(() => g.IsHeld); } // L05 expression tree capture
    static void Expr(System.Linq.Expressions.Expression<Func<bool>> e) { }

    // iterators and async
    public static IEnumerable<int> I01(Lock l) { Guard g = l.Enter(); Held = g; yield return 1; } // I01 clean
    public static IEnumerable<int> I02(Lock l) { Guard g = l.Enter(); yield return 1; Held = g; } // I02 clean? (local hoisted)
    public static async Task<Guard> T01(Lock l) { Guard g = l.Enter(); await Task.Yield(); return g; } // T01 clean
    public static async Task T02(Lock l) { var vt = l.EnterAsync(); var a = await vt; var b = await vt; } // T02 first reported
    public static async Task T03(Lock l) { var vt = l.EnterAsync(); var t1 = vt.AsTask(); var t2 = vt.AsTask(); var a = await t1; var b = await t2; } // T03
    public static async Task T04(Lock l) { var vt = l.EnterAsync(); var t1 = vt.Preserve(); var a = await t1; } // T04 Preserve
    public static async Task T05(Lock l) { var t = l.EnterTaskAsync(); var w = Task.WhenAny(t); var a = await await w; var b = await t; } // T05
    public static async Task T06(Lock l) { var t = l.EnterTaskAsync(); var a = await t.WaitAsync(CancellationToken.None); var b = await t; } // T06
    public static async Task T07(Lock l) { var t = l.EnterTaskAsync(); var c = t.ContinueWith(x => x.Result); var b = await t; } // T07
    public static async Task T08(Lock l) { var t = l.EnterTaskAsync(); var aw = t.GetAwaiter(); var a = aw.GetResult(); var b = aw.GetResult(); } // T08
    public static async Task T09(Lock l) { var vt = l.EnterAsync(); var aw = vt.GetAwaiter(); var a = aw.GetResult(); var b = aw.GetResult(); } // T09
    public static async Task T10(Lock l) { var t = l.EnterTaskAsync(); var cfg = t.ConfigureAwait(false); var a = await cfg; var b = await cfg; } // T10

    // misc reads
    public static void M01(Lock l) { Guard g = l.Enter(); object o = g; Guard a = (Guard)o; } // M01 unbox reported
    public static void M02(Lock l) { Guard? m = l.Enter(); Guard a = m.Value; Guard b = m.Value; } // M02
    public static void M03(Lock l) { Guard? m = l.Enter(); Guard a = m.GetValueOrDefault(); Guard b = m!.Value; } // M03
    public static void M04(Lock l) { var kv = KeyValuePair.Create(1, l.Enter()); var (k, v) = kv; Guard v2 = kv.Value; } // M04
    public static void M05(Lock l) { Guard g = l.Enter(); Guard a = g switch { var x => x }; g.Dispose(); } // M05
    public static void M06(Lock l) { Guard g = l.Enter(); ref Guard r = ref g; Guard c = r; } // M06
    public static void M07(Lock l, bool c) { Guard g = l.Enter(); Guard h = l.Enter(); Held = c ? g : h; h.Dispose(); } // M07
    public static void M08(Lock l) { Guard g = l.Enter(); var t = (g, 1); t.Item1.Dispose(); g.Dispose(); } // M08
    public static void M09(Lock l) { Guard g = l.Enter(); Guard[] arr = [g]; g.Dispose(); } // M09
    public static void M10(Lock l) { Guard g = l.Enter(); Span<Guard> s = [g]; g.Dispose(); } // M10
    public static void M11(Lock l) { Guard g = l.Enter(); Params(g); g.Dispose(); } // M11
    static void Params(params ReadOnlySpan<Guard> gs) { }
    public static void M12(Lock l) { Guard g = l.Enter(); var d = (IDisposable)g; d.Dispose(); g.Dispose(); } // M12
    public static void M13(Lock l) { Guard g = l.Enter(); Func<object?, bool> f = g.Equals; g.Dispose(); } // M13
    public static void M14(Lock l) { Guard g = l.Enter(); string s = g.ToString(); string t = $"{g}"; g.Dispose(); } // M14
    public static void M15(Lock l) { Guard g = l.Enter(); bool e = g.Equals(Held); } // M15
    public static void M16(Lock l) { Guard g = l.Enter(); Held = g; Held2 = g; } // M16
}

public static class RTH1
{
    public static Lock A01() { var l = new Lock(); var a = new { G = l.Enter() }; Guard x = a.G; Guard y = a.G; x.Dispose(); y.Dispose(); return l; }
    public static Lock M15() { var l = new Lock(); Guard g = l.Enter(); g.Dispose(); return l; }
}
