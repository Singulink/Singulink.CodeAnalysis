using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Singulink.CodeAnalysis;

// ---- a marked deque of my own
public sealed class Deque<[NonCopyable] T>
{
    private T[] _items = new T[8];
    private int _head, _count;
    public int Count => _count;
    public void Enqueue(T item)
    {
        if (_count == _items.Length) Grow();
        _items[(_head + _count++) % _items.Length] = NonCopyable.Move(ref item);
    }
    public T Dequeue() { T item = NonCopyable.Move(ref _items[_head]); _head = (_head + 1) % _items.Length; _count--; return item; }  // W01
    public bool TryDequeue(out T item)
    {
        if (_count == 0) { item = default!; return false; }
        item = Dequeue();
        return true;
    }
    public ref T PeekRef() => ref _items[_head];
    private void Grow()
    {
        var n = new T[_items.Length * 2];
        for (int i = 0; i < _count; i++) n[i] = NonCopyable.Move(ref _items[(_head + i) % _items.Length]);
        _items = n; _head = 0;
    }
}

// ---- an async lock with a queue of completion sources
public sealed class AsyncLock
{
    private readonly Deque<TaskCompletionSource<LockGuard>> _waiters = new();
    private bool _held;

    public Task<LockGuard> EnterAsync()
    {
        lock (_waiters)
        {
            if (!_held) { _held = true; return Task.FromResult(new LockGuard(this)); }        // X01
            var tcs = new TaskCompletionSource<LockGuard>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);                                                              // X02
            return tcs.Task;                                                                    // (known)
        }
    }

    internal void Release()
    {
        TaskCompletionSource<LockGuard>? next = null;
        lock (_waiters)
        {
            if (_waiters.TryDequeue(out next)) { }                                             // X03
            else _held = false;
        }
        next?.SetResult(new LockGuard(this));                                                   // X04
    }

    internal void Release2()
    {
        lock (_waiters)
        {
            if (_waiters.Count > 0) _waiters.Dequeue().SetResult(new LockGuard(this));          // X05
            else _held = false;
        }
    }

    internal void CancelFirst()
    {
        ref var first = ref _waiters.PeekRef();
        first.TrySetCanceled();                                                                 // X06
    }
}

[NonCopyable]
public struct LockGuard : IDisposable
{
    private AsyncLock? _owner;
    public LockGuard(AsyncLock owner) => _owner = owner;
    public void Dispose() { var o = _owner; _owner = null; o?.Release(); }
}

// ---- a waiter class over a value task source
public sealed class Waiter : IValueTaskSource<LockGuard>
{
    private ManualResetValueTaskSourceCore<LockGuard> _core;
    public ValueTask<LockGuard> Task => new(this, _core.Version);                               // X10
    public void Complete(AsyncLock owner) => _core.SetResult(new LockGuard(owner));            // X11
    public LockGuard GetResult(short token) => _core.GetResult(token);                          // X12 (known: suppressed)
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> c, object? s, short t, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, t, f);
}

public sealed class ValueLock
{
    private readonly Deque<Waiter> _waiters = new();
    private readonly Stack<Waiter> _pool = new();

    public ValueTask<LockGuard> EnterAsync()
    {
        var w = _pool.Count > 0 ? _pool.Pop() : new Waiter();                                  // X13
        _waiters.Enqueue(w);                                                                    // X14
        return w.Task;                                                                          // X15
    }

    public void Release(AsyncLock owner)
    {
        if (_waiters.TryDequeue(out var w)) w.Complete(owner);                                  // X16
    }
}

// ---- a generic waiter over IValueTaskSource<T>
public sealed class GWaiter<[NonCopyable] T> : IValueTaskSource<T>
{
    private ManualResetValueTaskSourceCore<T> _core;                                           // X20
    public ValueTask<T> Task => new(this, _core.Version);                                       // X21
    public void Complete(T value) => _core.SetResult(NonCopyable.Move(ref value));              // X22
    public T GetResult(short token) => _core.GetResult(token);                                  // X23
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> c, object? s, short t, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, t, f);
}

public sealed class UseGWaiter
{
    public async Task Run(AsyncLock l)
    {
        var w = new GWaiter<LockGuard>();
        w.Complete(new LockGuard(l));                                                           // X24
        using var g = await w.Task;                                                             // X25
    }
}

// ---- tasks started earlier
public sealed class StartEarly
{
    public async Task Run(AsyncLock a, AsyncLock b)
    {
        var pa = a.EnterAsync();                                                                // Y01
        var pb = b.EnterAsync();
        await Task.Yield();
        using var ga = await NonCopyable.Move(ref pa);                                          // Y02
        using var gb = await NonCopyable.Move(ref pb);                                          // Y03
        var pc = a.EnterAsync();
        if (await Task.WhenAny(pc, Task.Delay(100)) == pc)                                      // Y04
        {
            using var gc = await NonCopyable.Move(ref pc);                                      // Y05
        }
        var list = new List<Task<LockGuard>> { a.EnterAsync(), b.EnterAsync() };              // Y06
        await Task.WhenAll((IEnumerable<Task>)list);                                            // Y07
        foreach (var t in list) { using var g = t.Result; }                                     // Y08 (stored, expected)
    }
}
