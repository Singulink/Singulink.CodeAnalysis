using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct LGuard : IDisposable
{
    private AsyncLock? _owner;
    public LGuard(AsyncLock owner) => _owner = owner;
    public void Dispose() { var o = _owner; _owner = null; o?.Release(); }
}

public sealed class Deque<[NonCopyable] T>
{
    private T[] _items = new T[8];
    private int _head, _count;
    public int Count => _count;
    public void Enqueue(T item) { _items[(_head + _count++) % _items.Length] = NonCopyable.Move(ref item); }
    public T Dequeue() { ref T slot = ref _items[_head]; _head = (_head + 1) % _items.Length; _count--; return NonCopyable.Move(ref slot); }
    public bool TryDequeue(out T item) { if (_count == 0) { item = default!; return false; } item = Dequeue(); return true; }
    public ref T PeekRef() => ref _items[_head];
}

public sealed class AsyncLock
{
    private readonly Deque<TaskCompletionSource<LGuard>> _waiters = new();
    private readonly Deque<Task<LGuard>> _pendingTasks = new();
    private bool _held;

    public Task<LGuard> LockAsync()
    {
        lock (_waiters)
        {
            if (!_held) { _held = true; return Task.FromResult(new LGuard(this)); }   // F01
            var tcs = new TaskCompletionSource<LGuard>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);                                                 // F02
            return tcs.Task;                                                       // F03 known
        }
    }

    internal void Release()
    {
        TaskCompletionSource<LGuard>? next = null;
        lock (_waiters) { if (_waiters.Count > 0) next = _waiters.Dequeue(); else _held = false; }   // F04
        next?.SetResult(new LGuard(this));                                         // F05
        if (_waiters.TryDequeue(out var other)) other.TrySetCanceled();             // F06
        _waiters.PeekRef().TrySetCanceled();                                       // F07
    }

    public void Track(Task<LGuard> t) => _pendingTasks.Enqueue(t);                 // F08
}

public static class LockUser
{
    public static List<Task<LGuard>> StartAll(AsyncLock[] locks)
    {
        var list = new List<Task<LGuard>>();
        foreach (var l in locks) list.Add(l.LockAsync());                          // F10
        return list;                                                               // F11
    }

    public static Task<LGuard>[] StartAllArray(AsyncLock[] locks)
    {
        var arr = new Task<LGuard>[locks.Length];
        for (int i = 0; i < locks.Length; i++) arr[i] = locks[i].LockAsync();      // F12
        return arr;                                                                // F13
    }

    public static Deque<Task<LGuard>> StartAllDeque(AsyncLock[] locks)
    {
        var q = new Deque<Task<LGuard>>();
        foreach (var l in locks) q.Enqueue(l.LockAsync());                         // F14
        return q;                                                                  // F15
    }

    public static async Task UseAll(AsyncLock a, AsyncLock b)
    {
        var tasks = new List<Task<LGuard>> { a.LockAsync(), b.LockAsync() };      // F20
        await Task.WhenAll(tasks);                                                 // F21
        var t1 = a.LockAsync(); var t2 = b.LockAsync();
        await Task.WhenAll(t1, t2);                                                // F22
        using var g1 = await NonCopyable.Move(ref t1);                             // F23
        using var g2 = await NonCopyable.Move(ref t2);                             // F24
        var t3 = a.LockAsync();
        var winner = await Task.WhenAny(t3, Task.Delay(1000));                     // F25
        if (winner == t3) { using var g3 = await NonCopyable.Move(ref t3); }       // F26
        var q = StartAllDeque([a, b]);
        using var h1 = await NonCopyable.Move(ref q).Dequeue();                    // F27 only one
        var t4 = a.LockAsync();
        var t5 = b.LockAsync();
        var any = await Task.WhenAny(t4, t5);                                      // F28 generic overload
        var all = await Task.WhenAll(t4, t5);                                      // F29 generic overload
    }

    public static async Task<LGuard> FirstOf(AsyncLock a, AsyncLock b)
    {
        var bag = new Deque<Task<LGuard>>();
        bag.Enqueue(a.LockAsync());                                                // F30
        bag.Enqueue(b.LockAsync());
        return await NonCopyable.Move(ref bag).Dequeue();                          // F31
    }
}

public sealed class PendingCache
{
    private readonly Dictionary<string, Task<LGuard>> _byKey = new();
    private readonly Deque<Task<LGuard>> _queue = new();

    public Task<LGuard> GetOrStart(string key, AsyncLock l)
    {
        if (_byKey.TryGetValue(key, out var t)) return t;                          // F40 correct to report
        var started = l.LockAsync();
        _byKey[key] = started;                                                     // F41
        return started;                                                            // F42 correct to report
    }

    public Task<LGuard> Next() => _queue.Dequeue();                                // F43 judgment call
}
