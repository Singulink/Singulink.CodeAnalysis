using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using TCS = System.Threading.Tasks.TaskCompletionSource<Guard>;

// Correct lock code.
public class P3
{
    private readonly object _sync = new();
    private readonly Queue<TCS> _q = new();
    private readonly Queue<(TCS, CancellationToken)> _qt = new();
    private readonly LinkedList<TCS> _ll = new();
    private readonly Queue<Waiter> _qw = new();
    private TCS? _single;
    private bool _free;
    private readonly Lock _lock = new();

    public Task<Guard> L01() { lock (_sync) { var s = new TCS(TaskCreationOptions.RunContinuationsAsynchronously); _q.Enqueue(s); return s.Task; } }   // L01 clean
    public Task<Guard> L02(CancellationToken ct) { lock (_sync) { if (_free) { _free = false; return Task.FromResult(_lock.Enter()); } var s = new TCS(); ct.Register(() => s.TrySetCanceled(ct)); _q.Enqueue(s); return s.Task; } }   // L02 clean
    public Task<Guard> L03(CancellationToken ct) { var s = new TCS(); _qt.Enqueue((s, ct)); return s.Task; }        // L03 false positive?
    public Task<Guard> L04(CancellationToken ct) { var s = new TCS(); var node = _ll.AddLast(s); ct.Register(() => { lock (_sync) _ll.Remove(node); s.TrySetCanceled(); }); return s.Task; }   // L04 ?
    public async Task<Guard> L05(CancellationToken ct) { var s = new TCS(); _q.Enqueue(s); using (ct.Register(() => s.TrySetCanceled())) return await s.Task.ConfigureAwait(false); }   // L05 clean
    public Task<Guard> L06(CancellationToken ct) { var s = new TCS(); _q.Enqueue(s); return s.Task.WaitAsync(ct); }  // L06 clean
    public ValueTask<Guard> L07() { var s = new TCS(); _single = s; return new(s.Task); }                          // L07 clean
    public Task<Guard> L08(CancellationToken ct) { var s = new TCS(); _q.Enqueue(s); ct.Register(() => s.TrySetCanceled()); if (s.Task.IsCanceled) Console.WriteLine(); return s.Task; }   // L08 reported: two reads
    public Task<Guard> L09(CancellationToken ct) { var s = new TCS(); var t = s.Task; _q.Enqueue(s); ct.Register(() => s.TrySetCanceled()); if (t.IsCanceled) Console.WriteLine(); return t; }   // L09 clean
    public Task<Guard> L10() { var s = new TCS(); Interlocked.CompareExchange(ref _single, s, null); return s.Task; }   // L10 clean
    public Task<Guard> L11(CancellationToken ct) { var s = new TCS(); _qw.Enqueue(new Waiter(s, ct)); return s.Task; }   // L11 clean
    public Task<Guard> L12(CancellationToken ct) { var s = new TCS(); ct.Register(() => { if (!s.Task.IsCompleted) s.TrySetCanceled(); }); return s.Task; }   // L12 reported (read in a lambda)
    public Task<Guard> L13(CancellationToken ct) { var s = new TCS(); _q.Enqueue(s); ct.UnsafeRegister(static (o, t) => ((TCS)o!).TrySetCanceled(t), s); return s.Task; }   // L13 clean
    public async Task<Guard> L14(CancellationToken ct) { var s = new TCS(); _q.Enqueue(s); await using var r = ct.Register(() => s.TrySetCanceled()); return await s.Task; }   // L14 clean
    public Task<Guard> L15(int k) { switch (k) { case 0: var s = new TCS(); _q.Enqueue(s); return s.Task; default: return Task.FromResult(_lock.Enter()); } }   // L15 clean
    public Task<Guard> L16(int k) { var s = new TCS(); switch (k) { case 0: _q.Enqueue(s); break; case 1: goto case 0; } return s.Task; }   // L16 reported (goto)
    public Task<Guard> L17() { TCS s; s = new(); _q.Enqueue(s); return s.Task; }                                  // L17 clean
    public Task<Guard> L18() { TCS s = new(); _q.Enqueue(s); return s.Task; }                                     // L18 clean
    public Task<Guard> L19() { TCS? s = new(); _q.Enqueue(s); return s.Task; }                                    // L19 clean
    public Task<Guard> L20() { object s = new TCS(); return ((TCS)s).Task; }                                      // L20 reported
    public Task<Guard> L21() { var s = new TCS() { }; return s.Task; }                                            // L21 reported (initializer)
    public Task<Guard> L22(bool b) { var s = b ? new TCS() : new TCS(); return s.Task; }                          // L22 reported
    public Task<Guard> L23() { var s = new Sub3(); return s.Task; }                                               // L23 reported (derived)
    public Task<Guard> L24() { TCS s = new Sub3(); return s.Task; }                                               // L24 reported (derived)
    public Task<Guard> L25() { var s = (TCS)new(); return s.Task; }                                               // L25 ?
    public Task<Guard> L26() { if (new object() is TCS s) return s.Task; return null!; }                          // L26 reported
    public Task<Guard> L27() { var s = new TCS(); var s2 = new TCS(); _q.Enqueue(s); _q.Enqueue(s2); return s2.Task; }   // L27 clean
    public Task<Guard> L28() { var s = new TCS(); s = new TCS(); return s.Task; }                                 // L28 reported (two assignments)
    public Task<Guard> L29() { dynamic d = new TCS(); _q.Enqueue(d); return d.Task; }                            // L29 reported
    public object L30() { var s = new TCS(); var arr = new[] { s }; object o = arr; return o; }                   // L30 clean (erasable)
    public object L31() { var s = new TCS(); var list = new List<TCS> { s }; object o = list; return o; }         // L31 reported
    public IEnumerable<object> L32() { var list = new List<TCS>(); return list; }                                 // L32 clean (erasable)

    private sealed record Waiter(TCS Source, CancellationToken Token);
}

// Subclasses of the erasable completion source.
public interface IProducer<out T> { T Get(); }
public sealed class Sub1 : TCS, IEnumerable<Task<Guard>>                                                          // S01 reported
{
    public IEnumerator<Task<Guard>> GetEnumerator() { yield break; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
public class Sub2<T> : TaskCompletionSource<T>, IProducer<Task<T>> { public Task<T> Get() => Task; }               // S02 reported
public class Sub3 : TCS, IDisposable { public void Dispose() { } }                                                 // S03 clean
public class Sub4 : TCS, IProducer<Guard> { public Guard Get() => default; }                                       // S04 ?
public class Sub5<T> : TaskCompletionSource<T>, IComparable<Sub5<T>> { public int CompareTo(Sub5<T>? o) => 0; }    // S05 clean
public class Sub6<T> : TaskCompletionSource<T>, IEnumerable<T> { public IEnumerator<T> GetEnumerator() { yield break; } IEnumerator IEnumerable.GetEnumerator() => GetEnumerator(); }   // S06 reported
public class Sub7 : TCS, IProducer<object> { public object Get() => Task; }                                        // S07 ?
public class Sub8 : TCS, IProducer<TCS> { public TCS Get() => this; }                                              // S08 ?

public class Sub6M<[NonCopyable] T> : TaskCompletionSource<T>, IEnumerable<T> { public IEnumerator<T> GetEnumerator() { yield break; } IEnumerator IEnumerable.GetEnumerator() => GetEnumerator(); }   // S09 reported
public class Sub2M<[NonCopyable] T> : TaskCompletionSource<T>, IProducer<Task<T>> { public Task<T> Get() => Task; }   // S10 reported
public static class P3Use
{
    public static object U01() => new Sub6<Guard>();                       // U01 reported
    public static object U02() { TaskCompletionSource<Guard> s = new Sub2<Guard>(); return s; }   // U02 reported
    public static object U03() => new Sub6M<List<Guard>>();                // U03 ?
}
