using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Singulink.CodeAnalysis;
using static Src;

unsafe class P06 : IValueTaskSource<int>
{
    SpinLock _spin = new SpinLock(false);
    GCHandle _handle;
    ManualResetValueTaskSourceCore<int> _core;
    readonly Lock _lock = new();
    Guard _guard;
    Guard[] _guards = new Guard[4];

    void Ordinary(int x, Memory<byte> mem, object o)
    {
        string s1 = $"a{x}b";                                     // S01 OK
        string s2 = string.Create(null, $"a{x}");                 // S02 OK
        var sb = new StringBuilder(); sb.Append($"a{x}");         // S03 OK
        Debug.Assert(x > 0, $"x={x}");                            // S04 OK
        Span<char> buf = stackalloc char[16];
        buf.TryWrite($"a{x}", out int w);                         // S05 OK
        Console.WriteLine($"{x}");                                // S06 OK

        bool taken = false;
        _spin.Enter(ref taken);                                   // S10 OK
        if (taken) _spin.Exit();                                  // S11 OK
        bool held = _spin.IsHeld;                                 // S12 OK

        _handle = GCHandle.Alloc(o);                              // S20 OK
        IntPtr p = GCHandle.ToIntPtr(_handle);                    // S21 FP?
        var back = GCHandle.FromIntPtr(p);                        // S22 OK
        object? t = back.Target;                                  // S23 OK
        if (_handle.IsAllocated) _handle.Free();                  // S24 OK
        var pinned = new PinnedGCHandle<byte[]>(new byte[1]);     // S25 OK
        byte* bp = pinned.GetAddressOfArrayData();                // S26 OK
        pinned.Dispose();                                         // S27 OK

        using (MemoryHandle mh = mem.Pin()) { void* vp = mh.Pointer; } // S30 OK
        MemoryHandle mh2 = mem.Pin();
        try { } finally { mh2.Dispose(); }                        // S31 OK

        using (ExecutionContext.SuppressFlow()) { }               // S40 OK
        AsyncFlowControl afc = ExecutionContext.SuppressFlow();
        afc.Undo();                                               // S41 OK

        lock (_lock) { }                                          // S50 OK
        using (_lock.EnterScope()) { }                            // S51 OK

        ref Guard r = ref _guards[0];                             // S60 OK
        r.Dispose();                                              // S61 OK
        _guards[1] = Enter();                                     // S62 OK
        Span<Guard> span = _guards;                               // S63 OK
        span[2].Touch();                                          // S64 OK
        ref Guard r2 = ref MemoryMarshal.GetArrayDataReference(_guards); // S65 OK
        ref Guard r3 = ref Unsafe.Add(ref r2, 1);                 // S66 OK
        Guard old = Interlocked.Exchange(ref _guards[0], Enter()); // S67 OK? (Exchange<T> where T: ... runtime throws)
        var pool = ArrayPool<Guard>.Shared;
        Guard[] rented = pool.Rent(4);                            // S68 OK
        pool.Return(rented, clearArray: true);                    // S69 OK
        Array.Clear(_guards);                                     // S70 OK
        int len = _guards.Length;                                 // S71 OK
        Span<Guard> sl = span.Slice(1);                           // S72 OK
        Span<Guard> sl2 = span[1..];                              // S73 OK
        ref Guard last = ref span[^1];                            // S74 OK
        Guard[] arr2 = new Guard[8];                              // S75 OK
        var list = new List<Guard>();
        list.Add(Enter());                                        // S76 OK
        ref Guard li = ref CollectionsMarshal.AsSpan(list)[0];    // S77 OK
        _guard = Enter();                                         // S78 OK
        _guard.Dispose();                                         // S79 OK
        using (_guard) { }                                        // S80 report (field)
        Guard g = Enter();
        _guard = NonCopyable.Move(ref g);                         // S81 OK
        Span<Guard> sa = stackalloc Guard[2];                     // S82 OK
        fixed (Guard* fp = _guards) { }                           // S83 OK (pointers)
        int sz = Unsafe.SizeOf<Guard>();                          // S84 OK
        int sz2 = sizeof(Guard);                                  // S85 OK
        bool isref = RuntimeHelpers.IsReferenceOrContainsReferences<Guard>(); // S86 OK
        Volatile.Write(ref _guards, new Guard[1]);                // S87 OK
        var vr = Volatile.Read(ref _guards);                      // S88 OK
        Guard[] gc = GC.AllocateUninitializedArray<Guard>(4);     // S89 OK
        ReadOnlySpan<Guard> ro = span;                            // S90 OK
        span.Clear();                                             // S91 OK
        span.Reverse();                                           // S92 OK
        Span<Guard> e = Span<Guard>.Empty;                        // S93 OK
        Span<Guard> one = new Span<Guard>(ref _guard);            // S94 OK
        Span<Guard> one2 = MemoryMarshal.CreateSpan(ref _guard, 1); // S95 OK
        ref Guard nr = ref Unsafe.NullRef<Guard>();               // S96 OK
        if (Unsafe.IsNullRef(ref nr)) { }                         // S97 OK
        Memory<Guard> m = _guards.AsMemory();                     // S98 OK
        ref Guard mr = ref m.Span[0];                             // S99 OK
    }

    async Task Ord2()
    {
        using var g = await EnterAsync();                         // T01 OK
        using Guard g2 = await EnterAsync().ConfigureAwait(false);// T02 OK
        var t = EnterTaskAsync();
        await Task.WhenAny(t, Task.Delay(100));                   // T03 OK
        if (t.IsCompleted) { }                                    // T04 OK
        Guard g3 = await NonCopyable.Move(ref t);                 // T05 OK
        ValueTask<Guard> vt = EnterAsync();
        if (vt.IsCompletedSuccessfully) { Guard g4 = NonCopyable.Move(ref vt).Result; } // T06 OK
        await Task.Yield();
        Task<Guard>? pending = EnterTaskAsync();
        try { await pending.WaitAsync(TimeSpan.FromSeconds(1)); } catch { } // T07 OK? waitasync of stored, discarded
        Guard g5 = await pending!;                                // T08 report
    }

    public int GetResult(short token) => _core.GetResult(token);  // U01 OK (int)
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token); // U02 OK
    public void OnCompleted(Action<object?> c, object? s, short token, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, token, f); // U03 OK
    void Reset() { _core.Reset(); _core.SetResult(1); short v = _core.Version; } // U04 OK
}

class GuardSource : IValueTaskSource<Guard>
{
    ManualResetValueTaskSourceCore<Guard> _core;
    public Guard GetResult(short token) => _core.GetResult(token);  // U10 report (documented)
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token); // U11 OK
    public void OnCompleted(Action<object?> c, object? s, short token, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, token, f); // U12 OK
    public ValueTask<Guard> Start() { _core.Reset(); _core.SetResult(Src.Enter()); return new ValueTask<Guard>(this, _core.Version); } // U13 OK
    public bool Run { set => _core.RunContinuationsAsynchronously = value; } // U14 OK
    public void Fail() => _core.SetException(new Exception()); // U15 OK
}

struct BadHolder { public SpinLock S; }                          // U20 report
