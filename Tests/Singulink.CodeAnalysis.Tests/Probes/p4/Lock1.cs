using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Singulink.CodeAnalysis;

// ---------- An async lock on TaskCompletionSource<Releaser> ----------
public sealed class TcsLock
{
    private readonly object _sync = new();
    private readonly Queue<TaskCompletionSource<Releaser>> _waiters = new();
    private bool _held;

    public Task<Releaser> LockAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (!_held)
            {
                _held = true;
                return Task.FromResult(new Releaser(this));                   // F01 OK
            }

            var tcs = new TaskCompletionSource<Releaser>(TaskCreationOptions.RunContinuationsAsynchronously); // F02 OK
            _waiters.Enqueue(tcs);                                            // F03 OK
            if (ct.CanBeCanceled)
                ct.Register(() => tcs.TrySetCanceled(ct));                    // F04 ? captures tcs
            return tcs.Task;                                                  // F05 known: SCA1031
        }
    }

    public bool TryLock(out Releaser releaser)
    {
        lock (_sync)
        {
            if (_held) { releaser = default; return false; }                 // F06 OK
            _held = true;
            releaser = new Releaser(this);                                    // F07 OK
            return true;
        }
    }

    public Releaser? TryLock()
    {
        lock (_sync)
        {
            if (_held) return null;                                           // F08 OK
            _held = true;
            return new Releaser(this);                                        // F09 OK
        }
    }

    internal void Release()
    {
        TaskCompletionSource<Releaser>? next = null;
        lock (_sync)
        {
            while (_waiters.TryDequeue(out var w))                           // F10 OK
            {
                if (!w.Task.IsCompleted) { next = w; break; }                 // F11 OK
            }
            if (next is null) _held = false;
        }
        next?.TrySetResult(new Releaser(this));                               // F12 OK
    }

    [NonCopyable]
    public struct Releaser : IDisposable
    {
        private TcsLock? _owner;
        internal Releaser(TcsLock owner) => _owner = owner;
        public readonly bool IsHeld => _owner is not null;
        public void Dispose() { var o = _owner; _owner = null; o?.Release(); }
    }
}

// ---------- An async lock on IValueTaskSource<Guard2> ----------
public sealed class VtsLock
{
    private readonly Waiter _waiter;
    private int _state;

    public VtsLock() => _waiter = new Waiter(this);

    public ValueTask<Guard2> LockAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            return new ValueTask<Guard2>(new Guard2(this));                   // G01 OK
        _waiter.Reset();
        return new ValueTask<Guard2>(_waiter, _waiter.Version);              // G02 OK (listed new)
    }

    public ValueTask<Guard2> LockAsync(CancellationToken ct) => LockAsync(); // G03 OK

    internal void Release() { if (Interlocked.Exchange(ref _state, 0) == 2) _waiter.Complete(new Guard2(this)); }

    private sealed class Waiter : IValueTaskSource<Guard2>
    {
        private readonly VtsLock _owner;
        private ManualResetValueTaskSourceCore<Guard2> _core;
        public Waiter(VtsLock owner) { _owner = owner; _core.RunContinuationsAsynchronously = true; }
        public short Version => _core.Version;
        public void Reset() => _core.Reset();
        public void Complete(Guard2 g) => _core.SetResult(g);                // G04 ? passes a by-value parameter
        public Guard2 GetResult(short token) => _core.GetResult(token);      // G05 known: suppress
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token); // G06 OK
        public void OnCompleted(Action<object?> c, object? s, short token, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, token, f); // G07 OK
    }
}

[NonCopyable]
public struct Guard2 : IDisposable
{
    private VtsLock? _owner;
    internal Guard2(VtsLock owner) => _owner = owner;
    public void Dispose() { var o = _owner; _owner = null; o?.Release(); }
}

// ---------- Users ----------
class Users1
{
    private readonly TcsLock _tl = new();
    private readonly VtsLock _vl = new();
    private Guard2 _held;
    private TcsLock.Releaser _rel;

    async Task Use(CancellationToken ct)
    {
        using (await _tl.LockAsync())                                         // H01 OK
        { }
        using var a = await _tl.LockAsync(ct).ConfigureAwait(false);         // H02 OK
        using var b = await _vl.LockAsync();                                  // H03 OK
        using var c = await _vl.LockAsync().ConfigureAwait(false);            // H04 OK
        using var d = await _tl.LockAsync().WaitAsync(TimeSpan.FromSeconds(1), ct); // H05 OK
        using var e = await _vl.LockAsync().AsTask().WaitAsync(ct);           // H06 OK
        if (_tl.TryLock(out var r)) { using (r) { } }                         // H07 ? using on out var
        if (_tl.TryLock() is { } r2) { r2.Dispose(); }                        // H08 OK
        using var r3 = _tl.TryLock();                                         // H09 ? Nullable using
        _held = await _vl.LockAsync();                                         // H10 OK
        _held.Dispose();                                                       // H11 OK
        _rel = await _tl.LockAsync();                                          // H12 OK
        var pending = _vl.LockAsync();                                         // H13 OK
        using var f = await NonCopyable.Move(ref pending);                     // H14 OK
        var ptask = _tl.LockAsync();                                           // H15 OK
        using var g = await NonCopyable.Move(ref ptask);                       // H16 OK
        await using var h = new AsyncGuardHolder(await _vl.LockAsync());       // H17 OK
        await Task.WhenAll(_tl.LockAsync(), Task.Delay(1));                   // H18 ? non-generic WhenAll
        var t1 = _tl.LockAsync();
        if (await Task.WhenAny(t1, Task.Delay(1000, ct)) == t1)              // H19 ordinary timeout pattern
        { using var held = await NonCopyable.Move(ref t1); }                   // H20 ?
        Guard2 local = await _vl.LockAsync();
        try { } finally { local.Dispose(); }                                   // H21 OK
        Consume(await _vl.LockAsync());                                        // H22 OK
        ConsumeIn(in local);                                                   // H23 OK
        ConsumeRef(ref local);                                                 // H24 OK
        _held = NonCopyable.Move(ref local);                                   // H25 OK
    }

    static void Consume(Guard2 g) => g.Dispose();
    static void ConsumeIn(in Guard2 g) { }
    static void ConsumeRef(ref Guard2 g) => g.Dispose();

    async Task<Guard2> Wrapper() => await _vl.LockAsync();                    // H26 OK
    ValueTask<Guard2> Forward() => _vl.LockAsync();                            // H27 OK
    async ValueTask<Guard2> Forward2(CancellationToken ct) { var g = await _vl.LockAsync(ct); return g; } // H28 OK
    Task<TcsLock.Releaser> Forward3() => _tl.LockAsync();                      // H29 OK
}

sealed class AsyncGuardHolder : IAsyncDisposable
{
    private Guard2 _g;
    public AsyncGuardHolder(Guard2 g) => _g = g;                              // H30 OK (by-value param into field)
    public ValueTask DisposeAsync() { _g.Dispose(); return default; }
}
