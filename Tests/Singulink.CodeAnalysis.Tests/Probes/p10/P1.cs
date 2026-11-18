using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using TCS = System.Threading.Tasks.TaskCompletionSource<Guard>;

// Forms of a read, aliases, the local itself.
public class P1
{
    private readonly Queue<TCS> _q = new();
    private TCS? _f;
    private object? _o;

    public Task<Guard> A01() { var s = new TCS(); _q.Enqueue(s); return s.Task; }                       // A01 clean
    public Task<Guard> A02() { var s = new TCS(); var x = s?.Task; return s.Task; }                     // A02 reported
    public Task<Guard> A03() { var s = new TCS(); var x = s!.Task; return s!.Task; }                    // A03 reported
    public Task<Guard> A04() { var s = new TCS(); var x = (s).Task; return s.Task; }                    // A04 reported
    public Task<Guard> A05() { var s = new TCS(); var x = ((TCS)s).Task; return s.Task; }               // A05 reported
    public Task<Guard> A06() { var s = new TCS(); if (s is { Task: var t }) { } return s.Task; }        // A06 reported
    public Task<Guard> A07() { var s = new TCS(); (Task<Guard> t, Task<Guard> _) = s; return s.Task; }                    // A07 reported
    public Task<Guard> A08() { var s = new TCS(); _ = nameof(s.Task); return s.Task; }                  // A08 reported (conservative)
    public Task<Guard> A09() { var s = new TCS(); Expression<Func<Task<Guard>>> e = () => s.Task; return s.Task; }   // A09 reported
    public Task<Guard> A10() { var s = new TCS(); var q = from i in new[] { 1 } select s.Task; return s.Task; }       // A10 reported
    public Task<Guard> A11() { var s = new TCS(); var str = $"{s.Task}"; return s.Task; }               // A11 reported
    public Task<Guard> A12() { var s = new TCS(); var l = new List<Task<Guard>> { s.Task }; return s.Task; }         // A12 reported
    public Task<Guard> A13() { var s = new TCS(); var o = s; return o.Task; }                           // A13 reported
    public Task<Guard> A14() { var s = new TCS(); ref var r = ref s; return r.Task; }                   // A14 reported
    public Task<Guard> A15() { var s = new TCS(); var t = (s, 1); return t.s.Task; }                    // A15 reported
    public Task<Guard> A16() { var s = new TCS(); var a = new[] { s }; return a[0].Task; }              // A16 reported
    public Task<Guard> A17() { var s = new TCS(); _f = s; return _f.Task; }                             // A17 reported
    public Task<Guard> A18() { var s = new TCS(); Func<Task<Guard>> g = () => s.Task; return g(); }     // A18 reported
    public Task<Guard> A19() { var s = new TCS(); Task<Guard> L() => s.Task; return L(); }              // A19 reported
    public Task<Guard> A20() { var s = new TCS(); var x = s.Task; return x; }                           // A20 clean
    public Task<Guard> A21(int k) { var s = new TCS(); Task<Guard> t = k switch { 1 => s.Task, _ => s.Task }; return t; }    // A21 reported (two reads)
    public Task<Guard> A22(bool b) { var s = new TCS(); return b ? s.Task : s.Task; }                   // A22 reported (two reads)
    public Task<Guard> A23() { var s = new TCS(); try { return s.Task; } finally { } }                  // A23 clean
    public Task<Guard> A24() { var s = new TCS(); for (int i = 0; i < 1; i++) { return s.Task; } return null!; }  // A24 reported (loop)
    public Task<Guard> A25() { var s = new TCS(); again: var t = s.Task; if (t is null) goto again; return t; }  // A25 reported (goto)
    public async Task<Guard> A26() { var s = new TCS(); _q.Enqueue(s); return await s.Task; }          // A26 clean
    public IEnumerable<Task<Guard>> A27() { var s = new TCS(); yield return s.Task; }                   // A27 reported
    public Task<Guard> A28() { var s = new TCS(); _o = s; return s.Task; }                              // A28 clean (erasable)
    public Task<Guard> A29() { var s = new TCS(); Interlocked.Exchange(ref _f, s); return s.Task; }     // A29 clean
    public Task<Guard> A30() { var s = new TCS(); Volatile.Write(ref _f, s); return s.Task; }           // A30 clean
    public Task<Guard> A31() { var s = new TCS(); var u = __makeref(s); return s.Task; }                // A31 reported
    public Task<Guard> A32() { var s = new TCS(); Ext.Touch(s); return s.Task; }                        // A32 clean
    public Task<Guard> A33() { var s = new TCS(); s.Touch2(); return s.Task; }                          // A33 clean
    public Task<Guard> A34() { var s = new TCS(); var t = s.GetTask(); return s.Task; }                 // A34 ?
    public Task<Guard> A35() { var s = new TCS(); return s.Task2; }                                     // A35 reported (extension property, stored)
    public Task<Guard> A36() { var s = new TCS(); var t = s.Task2; return s.Task; }                     // A36 ?
    public Task<Guard> A37() { var s = new TCS(); if (s is { Task2: var t }) { } return s.Task; }       // A37 reported
    public Task<Guard> A38() { var s = new TCS(); Action a = () => Ext.Touch(s); return s.Task; }       // A38 clean
    public Task<Guard> A39() { var s = new TCS(); Action a = () => s = new TCS(); return s.Task; }      // A39 reported
    public Task<Guard> A40() { var s = new TCS(); Func<TCS> a = () => s; return a().Task; }             // A40 reported
    public Task<Guard> A41() { var s = new TCS(); TCS L() => s; var s2 = L(); return s2.Task; }         // A41 reported
    public Task<Guard> A42() { var s = new TCS(); Action a = () => { Action b = () => _ = s.Task; }; return s.Task; }   // A42 reported
    public Task<Guard> A43() { var s = new TCS(); var x = s.Task.Id; return s.Task; }                   // A43 reported (two reads)
    public Task<Guard> A44() { var s = new TCS(); Ext.Out(out s); return s.Task; }                      // A44 reported
    public Task<Guard> A45() { var s = new TCS(); (s, var k) = (new TCS(), 1); return s.Task; }        // A45 reported
    public Task<Guard> A46() { var s = new TCS(); s ??= new TCS(); return s.Task; }                     // A46 reported
    public Task<Guard> A47() { var s = new TCS(); Ext.Ref(ref s); return s.Task; }                      // A47 reported
    public Task<Guard> A48() { var s = new TCS(); Ext.In(in s); return s.Task; }                        // A48 reported
    public Task<Guard> A49() { var s = new TCS(); var d = (dynamic)s; return s.Task; }                  // A49 ?
    public Task<Guard> A50() { var s = new TCS(); Ext.Generic(s); return s.Task; }                      // A50 ?
    public Task<Guard> A51() { var s = new TCS(); Ext.Marked(s); return s.Task; }                       // A51 ?
    public Task<Guard> A52() { var s = new TCS(); Ext.Kept(s); return s.Task; }                         // A52 clean
    public Task<Guard> A53() { var s = new TCS(); TCS[] arr = [s]; return s.Task; }                     // A53 clean
    public Task<Guard> A54() { var s = new TCS(); bool n = s is null || s == null; return s.Task; }     // A54 clean
    public Task<Guard> A55() { var s = new TCS(); Ext.Params(s, s); return s.Task; }                    // A55 clean
    public Task<Guard> A56() { var s = new TCS(); var u = Unsafe(ref s); return s.Task; }               // A56 reported
    private static int Unsafe(ref TCS s) => 0;
}

public static class Ext
{
    public static void Touch(TCS s) { }
    public static void Touch2(this TCS s) { }
    public static Task<Guard> GetTask(this TCS s) => s.Task;                     // X01 reported
    public static void Out(out TCS s) => s = new TCS();
    public static void Ref(ref TCS s) { }
    public static void In(in TCS s) { }
    public static void Generic<T>(T v) { }
    public static void Marked<[NonCopyable] T>(T v) { }
    public static void Kept([Kept] TCS s) => s_kept = s;
    public static void Params(params TCS[] s) { }
    private static TCS? s_kept;

    extension(TCS s)
    {
        public Task<Guard> Task2 => s.Task;                                       // X03
    }
}
