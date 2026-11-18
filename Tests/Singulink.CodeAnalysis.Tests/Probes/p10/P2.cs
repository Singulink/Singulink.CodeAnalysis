using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using TCS = System.Threading.Tasks.TaskCompletionSource<Guard>;

// Callers of the lock and other holders. Each returns the lock so that Program can count releases.
public static class RT2
{
    private static Task<Guard>? s_task;

    public static Lock B01() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); Guard a = t.Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B01 reported
    public static Lock B02() { var al = new AsyncLock(); var t = al.Enter(); var t2 = t; al.GrantAll(); Guard a = t.Result; Guard b = t2.Result; a.Dispose(); b.Dispose(); return al.L; }   // B02 reported
    public static Lock B03() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var w = t.WaitAsync(TimeSpan.FromSeconds(5)); Guard a = w.Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B03 reported
    public static Lock B04() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var v1 = new ValueTask<Guard>(t); var v2 = new ValueTask<Guard>(t); Guard a = v1.Result; Guard b = v2.Result; a.Dispose(); b.Dispose(); return al.L; }   // B04 reported
    public static Lock B05() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var v = new ValueTask<Guard>(t); var t2 = v.AsTask(); Guard a = t2.Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B05 reported
    public static Lock B06() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var list = new List<Task<Guard>> { t }; Guard a = list[0].Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B06 reported
    public static Lock B07() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var w = Task.WhenAny(t, t).Result; Guard a = w.Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B07 reported
    public static Lock B08() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); Task<Guard>[] arr = [t]; Guard a = Task.WhenAny(arr.AsSpan()).Result.Result; Guard b = arr[0].Result; a.Dispose(); b.Dispose(); return al.L; }   // B08 reported
    public static Lock B09() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var aw = t.GetAwaiter(); Guard a = aw.GetResult(); Guard b = aw.GetResult(); a.Dispose(); b.Dispose(); return al.L; }   // B09 reported
    public static Lock B10() { var al = new AsyncLock(); al.Enter(); al.GrantAll(); return al.L; }   // B10 a leak, no finding expected
    public static Lock B11() { var al = new AsyncLock(); var aw = al.Enter().GetAwaiter(); al.GrantAll(); Guard a = aw.GetResult(); Guard b = aw.GetResult(); a.Dispose(); b.Dispose(); return al.L; }   // B11 reported
    public static Lock B12() { var al = new AsyncLock(); var c = al.Enter().ConfigureAwait(false); al.GrantAll(); Guard a = c.GetAwaiter().GetResult(); Guard b = c.GetAwaiter().GetResult(); a.Dispose(); b.Dispose(); return al.L; }   // B12 reported
    public static Lock B13() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var s2 = new TCS(); s2.SetFromTask(t); Guard a = s2.Task.Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B13 reported
    public static Lock B14() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var c = t.ContinueWith(x => 1); Guard b = t.Result; b.Dispose(); return al.L; }   // B14 reported (ContinueWith is not listed)
    public static Lock B15() { var al = new AsyncLock(); var (a1, a2) = al.Enter(); al.GrantAll(); Guard a = a1.Result; Guard b = a2.Result; a.Dispose(); b.Dispose(); return al.L; }   // B15 reported
    public static Lock B16() { var al = new AsyncLock(); var s = new TCS(); Ext2.Complete(s, al.L); (Task<Guard> a1, Task<Guard> a2) = s; Guard a = a1.Result; Guard b = a2.Result; a.Dispose(); b.Dispose(); return al.L; }   // B16 reported
    public static Lock B17() { var al = new AsyncLock(); var s = new TCS(); Ext2.Complete(s, al.L); if (s is { Task: var a1, Task: var a2 }) { Guard a = a1.Result; Guard b = a2.Result; a.Dispose(); b.Dispose(); } return al.L; }   // B17 reported
    public static Lock B18() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); s_task = t; Guard b = t.Result; b.Dispose(); Guard c = s_task.Result; c.Dispose(); return al.L; }   // B18 reported
    public static Lock B19() { var al = new AsyncLock(); var t = Again(al); al.GrantAll(); Guard a = t.Result; Guard b = s_task!.Result; a.Dispose(); b.Dispose(); return al.L; }   // B19 reported somewhere
    public static Lock B20() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); IAsyncResult r = t; Guard a = ((Task<Guard>)r).Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B20 reported
    public static Lock B21() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var vt = new ValueTask<Guard>(t); var p = vt.Preserve(); Guard a = p.Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B21 reported
    public static Lock B22() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); Task plain = t; Guard a = ((Task<Guard>)plain).Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B22 reported
    public static Lock B23() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); Guard a = Run(t).Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B23 reported
    public static Lock B24() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); Guard a = Task.Run(() => t).Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B24 reported
    public static Lock B25() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); Guard a = Task.WhenAny(new E(t)).Result.Result; Guard b = t.Result; a.Dispose(); b.Dispose(); return al.L; }   // B25 reported
    public static Lock B26() { var al = new AsyncLock(); var t = al.Enter(); al.GrantAll(); var ts = Task.WhenAll(t, t).Result; Guard b = t.Result; b.Dispose(); return al.L; }   // B26 reported (generic WhenAll not listed)

    private static Task<Guard> Run(Task<Guard> t) => Await(t);
    private static async Task<Guard> Await(Task<Guard> t) => await t;                       // B30 reported: a parameter
    private static Task<Guard> Again(AsyncLock al) { var t = al.Enter(); s_task = t; return t; }   // B31 reported

    private sealed class E(Task<Guard> t) : IEnumerable<Task<Guard>>
    {
        public IEnumerator<Task<Guard>> GetEnumerator() { yield return t; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public static class Ext2
{
    public static void Complete(TCS s, Lock l) => s.TrySetResult(l.Enter());
    public static void Deconstruct(this TCS s, out Task<Guard> a, out Task<Guard> b) { a = s.Task; b = s.Task; }
    public static void Deconstruct(this Task<Guard> t, out Task<Guard> a, out Task<Guard> b) { a = t; b = t; }
}
