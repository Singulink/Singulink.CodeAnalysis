using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class CN1
{
    static Task<Guard> s_cached = null!;
    static async Task K01(Lock l) { var t = l.EnterTaskAsync(); var aw = t.GetAwaiter(); using var a = await t; using var b = aw.GetResult(); }   // K01 one of the two reported
    static async Task K02(Lock l) { var t = l.EnterTaskAsync(); var c = t.ConfigureAwait(false); using var a = await t; using var b = await c; }   // K02
    static async Task K04(Lock l) { var t = l.EnterTaskAsync(); Task task = t; using var a = await t; using var b = await (Task<Guard>)task; }   // K04
    static void K05(Lock l) { var list = new List<Guard> { l.Enter() }; ref Guard r = ref CollectionsMarshal.AsSpan(list)[0]; Guard a = list[0]; r.Dispose(); a.Dispose(); }   // K05
    static async Task K14(Lock l, bool c) { Task<Guard> t = c ? l.EnterTaskAsync() : s_cached; using var a = await t; }   // K14 reported
    static async Task K16(Lock l) { var t = l.EnterTaskAsync(); t.Wait(); using var a = t.Result; }   // K16 clean
    static async Task K18(Lock l) { var t = Task.FromResult(l.Enter()); using var a = await t; }   // K18 clean
    static async Task K19(Lock l) { var vt = l.EnterAsync(); var t = vt.AsTask(); using var a = await t; }   // K19 clean
    static async Task K20(Lock l) { var vt = l.EnterAsync(); var p = vt.Preserve(); using var a = await vt; using var b = await p; }   // K20
    static async Task K21(Lock l) { var vt = l.EnterAsync(); var t = vt.AsTask(); using var b = vt.Result; using var a = await t; }   // K21
    static async Task K22(Lock l) { var t = l.EnterTaskAsync(); var arr = new[] { t }; using var a = await t; using var b = await arr[0]; }   // K22
    static async Task K23(Lock l) { var t = l.EnterTaskAsync(); var tup = (t, 1); using var a = await t; using var b = await tup.Item1; }   // K23
    static async Task K24(Lock l) { var t = l.EnterTaskAsync(); Task<Guard>? u = null; u ??= t; using var a = await t; using var b = await u; }   // K24
    static async Task K25(Lock l) { var t = l.EnterTaskAsync(); var w = Task.WhenAny(t); using var a = await t; using var b = await await w; }   // K25
    static async Task K26(Lock l) { var t = l.EnterTaskAsync(); Func<Task<Guard>> f = () => t; using var a = await t; using var b = await f(); }   // K26
    static async Task K27(Lock l) { var t = l.EnterTaskAsync(); object o = t; using var a = await t; }   // K27
    static async Task K28(Lock l) { var t = l.EnterTaskAsync(); var list = new List<Task<Guard>> { t }; using var a = await t; }   // K28
    static async Task K29(Lock l) { var t = l.EnterTaskAsync(); var vt = new ValueTask<Guard>(t); using var a = await t; using var b = await vt; }   // K29
    static async Task K30(Lock l) { var t = l.EnterTaskAsync(); var (x, y) = (t, 1); using var a = await t; using var b = await x; }   // K30
    static async Task K31(Lock l) { var t = l.EnterTaskAsync(); if (t is { } u) { using var a = await t; using var b = await u; } }   // K31
    static async Task K32(Lock l) { var t = l.EnterTaskAsync(); var s = t switch { var u => u }; using var a = await t; using var b = await s; }   // K32
    static async Task K33(Lock l) { var t = l.EnterTaskAsync(); Task<Guard> u = t ?? s_cached; using var a = await t; using var b = await u; }   // K33
    static async Task K34(Lock l) { var t = l.EnterTaskAsync(); using var a = await (true ? t : t); }   // K34 clean?
    static async Task K35(Lock l) { var t = l.EnterTaskAsync(); _ = t.IsCompleted ? 1 : 0; using var a = await t; }   // K35 clean
    static async Task K36(Lock l) { var t = l.EnterTaskAsync(); var e = t.Exception; using var a = await t; }   // K36 clean
    static async Task K37(Lock l) { var t = l.EnterTaskAsync(); var h = t.AsyncState; using var a = await t; }   // K37 clean
    static async Task K38(Lock l) { var t = l.EnterTaskAsync(); var c = t.WaitAsync(TimeSpan.FromSeconds(1)); using var a = await t; using var b = await c; }   // K38
}
