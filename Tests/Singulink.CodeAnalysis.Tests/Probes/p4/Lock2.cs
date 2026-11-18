using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// An async lock whose waiter queue is the library's own generic collection with a marked type parameter.
public sealed class Deque<[NonCopyable] T>
{
    private T[] _items = new T[8];
    private int _head, _count;
    public int Count => _count;
    public void Enqueue(T item) { _items[(_head + _count++) % _items.Length] = NonCopyable.Move(ref item); }
    public T Dequeue() { _count--; ref T slot = ref _items[_head]; _head = (_head + 1) % _items.Length; return NonCopyable.Move(ref slot); }
    public ref T PeekRef() => ref _items[_head];
}

public sealed class DequeLock
{
    private readonly Deque<TaskCompletionSource<DGuard>> _waiters = new();
    private bool _held;

    public Task<DGuard> LockAsync()
    {
        lock (_waiters)
        {
            if (!_held) { _held = true; return Task.FromResult(new DGuard(this)); }   // Q01 OK
            var tcs = new TaskCompletionSource<DGuard>();
            _waiters.Enqueue(tcs);                                                     // Q02 FP? local into marked T
            return tcs.Task;                                                           // Q03 known SCA1031
        }
    }

    public Task<DGuard> LockAsync2()
    {
        lock (_waiters)
        {
            var tcs = new TaskCompletionSource<DGuard>();
            var task = tcs.Task;
            _waiters.Enqueue(NonCopyable.Move(ref tcs));                               // Q04 OK
            return task;                                                               // Q05 known SCA1031
        }
    }

    internal void Release()
    {
        TaskCompletionSource<DGuard>? next = null;
        lock (_waiters)
        {
            if (_waiters.Count == 0) { _held = false; return; }
            next = _waiters.Dequeue();                                                 // Q06 OK
        }
        next.SetResult(new DGuard(this));                                              // Q07 OK
        _waiters.PeekRef().TrySetCanceled();                                           // Q08 OK in place
    }
}

[NonCopyable]
public struct DGuard : IDisposable
{
    private DequeLock? _l;
    internal DGuard(DequeLock l) => _l = l;
    public void Dispose() { var l = _l; _l = null; l?.Release(); }
}
