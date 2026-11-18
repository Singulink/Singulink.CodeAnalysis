using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct AsyncGuard : IAsyncDisposable
{
    private int _state;

    public async ValueTask DisposeAsync() { await Task.Yield(); _state = 0; }          // N01 async on a guard: runs on a copy of this
    public IEnumerable<int> Steps() { yield return _state; }                           // N02 iterator on a guard: copies this
    public ValueTask DisposeFastAsync() { _state = 0; return default; }                // N03 not async: fine
}

internal class Guards : List<Guard> { }

internal interface IMove { T Take<[NonCopyable] T>(ref T x); }
internal class Base1 : IMove { public T Take<[NonCopyable] T>(ref T x) => NonCopyable.Move(ref x); }
internal class Derived1 : Base1, IMove { T IMove.Take<T>(ref T x) => x; }             // N04

internal struct PrimS(Guard g) { public void Use() => g.Touch(); }                    // N05

internal sealed class Waiter : IValueTaskSource<Guard>
{
    private ManualResetValueTaskSourceCore<Guard> _core;
    public Guard GetResult(short token) => default;
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> c, object? s, short token, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, token, f);
}

internal class Probe11
{
    private Action _act = null!;
    private Task<Guard>? _a, _b;

    private static IEnumerable<int> Run(Guard g) { g.Dispose(); yield break; }        // N06
    private static IEnumerator<int> RunOnce(Guard g) { g.Dispose(); yield break; }    // N07

    private Guard Captured()
    {
        Guard g = Owner.Enter();
        _act = () => g.Dispose();
        return g;                                                                      // N08
    }

    private async Task Use(Guards guards, Stack<Task<Guard>> s1, Waiter waiter, CancellationToken token)
    {
        Guard g1 = await Interlocked.Exchange(ref _a, null)!;                          // N09
        Guard g3 = await s1.Pop();                                                     // N10
        Guard top = new Stack<Guard>().Pop();                                          // N11 allowed
        Guard a = guards[0];                                                           // N12
        IList il = guards;                                                             // N13
        object o = guards;                                                             // N14
        token.UnsafeRegister(static state => { }, waiter);                             // N15 a sealed checked class passed as object
        object w = waiter;                                                             // N16
    }
}
