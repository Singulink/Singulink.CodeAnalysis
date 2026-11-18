using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class AsyncLock10
{
    private readonly object _sync = new();
    private readonly Queue<TaskCompletionSource<Releaser>> _waiters = new();
    private bool _held;

    public ValueTask<Releaser> LockAsync()
    {
        lock (_sync)
        {
            if (!_held) { _held = true; return new ValueTask<Releaser>(new Releaser(this)); }   // L01
            var tcs = new TaskCompletionSource<Releaser>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);                                                             // L02
            var task = tcs.Task;
            return new ValueTask<Releaser>(NonCopyable.Move(ref task));                        // L03
        }
    }

    public async ValueTask<Releaser> LockSlowAsync(CancellationToken ct)
    {
        var r = await LockAsync().ConfigureAwait(false);                                       // L04
        ct.ThrowIfCancellationRequested();
        return r;                                                                              // L05
    }

    public async Task<Releaser> LockTaskAsync() => await LockAsync();                          // L06
    public Releaser Lock() => new Releaser(this);                                              // L07
    public Releaser? TryLock() => _held ? null : new Releaser(this);                           // L08

    internal void Release()
    {
        TaskCompletionSource<Releaser>? next = null;
        lock (_sync) { if (_waiters.Count > 0) next = _waiters.Dequeue(); else _held = false; }  // L09
        next?.SetResult(new Releaser(this));                                                   // L10
    }

    [NonCopyable]
    public struct Releaser : IDisposable
    {
        private AsyncLock10? _owner;
        internal Releaser(AsyncLock10 owner) => _owner = owner;
        public void Dispose() { var o = _owner; _owner = null; o?.Release(); }
    }
}

class Use10
{
    private readonly AsyncLock10 _lock = new();
    private AsyncLock10.Releaser _heldField;
    private readonly Lock _lockObj = new();
    private SpinLock _spin = new(false);
    private Guard[] _guards = new Guard[4];
    private readonly Dictionary<int, Guard> _dict = new();
    private readonly List<Guard> _list = new();
    private Task<Guard>? _pending;

    async Task M(CancellationToken ct)
    {
        using (await _lock.LockAsync()) { }                                                    // L20
        using var r = await _lock.LockAsync().ConfigureAwait(false);                           // L21
        using (_lock.Lock()) { }                                                               // L22
        var r2 = _lock.Lock(); try { } finally { r2.Dispose(); }                               // L23
        _heldField = await _lock.LockAsync();                                                  // L24
        _heldField.Dispose();                                                                  // L25
        using var r3 = await _lock.LockAsync().AsTask().WaitAsync(ct);                         // L26
        if (_lock.TryLock() is { } t) { try { } finally { t.Dispose(); } }                     // L27
        using (_lockObj.EnterScope()) { }                                                      // L28
        lock (_lockObj) { }                                                                    // L29
        bool taken = false; _spin.Enter(ref taken); if (taken) _spin.Exit();                   // L30
        await Task.WhenAll(_lock.LockAsync().AsTask(), _lock.LockAsync().AsTask());            // L31
        var both = await Task.WhenAll(_lock.LockAsync().AsTask(), _lock.LockAsync().AsTask()); // L32 (documented: array of copies)
    }

    void Arrays()
    {
        for (int i = 0; i < _guards.Length; i++) _guards[i] = Owner.Enter();                   // L40
        _guards[0].Dispose();                                                                  // L41
        ref Guard g = ref _guards[1];                                                          // L42
        Span<Guard> span = _guards;                                                            // L43
        span[0].Dispose();                                                                     // L44
        foreach (ref Guard x in span) x.Dispose();                                             // L45
        foreach (ref Guard x in _guards.AsSpan()) x.Dispose();                                 // L46
        Array.Clear(_guards);                                                                  // L47
        Guard[] pooled = ArrayPool<Guard>.Shared.Rent(4);                                      // L48
        ArrayPool<Guard>.Shared.Return(pooled, true);                                          // L49
        ref Guard first = ref MemoryMarshal.GetArrayDataReference(_guards);                    // L50
        ref Guard second = ref Unsafe.Add(ref first, 1);                                       // L51
        Span<Guard> stack = stackalloc Guard[2];                                               // L52
        stack[0] = Owner.Enter();                                                              // L53
        ReadOnlySpan<Guard> ro = _guards;                                                      // L54
        ro[0].Touch();                                                                         // L55 (copy through a readonly ref: reported)
        Guard[] made = [Owner.Enter(), Owner.Enter()];                                         // L56
        Span<Guard> sl = span[1..];                                                            // L57
        int n = _guards.Length;                                                                // L58
        _guards = new Guard[8];                                                                // L59
        Array.Resize(ref _guards, 16);                                                         // L60 SCA1020 (copies)
    }

    void Collections()
    {
        _dict.Add(1, Owner.Enter());                                                           // L70
        CollectionsMarshal.GetValueRefOrNullRef(_dict, 1).Dispose();                           // L71
        if (_dict.Remove(1, out var g)) g.Dispose();                                           // L72
        _list.Add(Owner.Enter());                                                              // L73
        CollectionsMarshal.AsSpan(_list)[0].Dispose();                                         // L74
        _list.RemoveAt(0);                                                                     // L75
        _list.Clear();                                                                         // L76
        bool has = _dict.ContainsKey(1);                                                       // L77
        int c = _list.Count;                                                                   // L78
        _pending = Owner.EnterTaskAsync();                                                     // L79
        var p = Interlocked.Exchange(ref _pending, null);                                      // L80
    }

    async Task<Guard> Later()
    {
        var pending = Owner.EnterTaskAsync();
        await Task.Yield();
        return await NonCopyable.Move(ref pending);                                            // L90
    }

    async ValueTask<Guard> ReturnLocal()
    {
        var g = Owner.Enter();
        await Task.Yield();
        return g;                                                                              // L91
    }

    Guard Helper(ref Guard g) { g.Touch(); return NonCopyable.Move(ref g); }                   // L92
}
