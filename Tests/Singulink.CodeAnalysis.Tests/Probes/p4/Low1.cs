using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable, InlineArray(4)]
public struct GuardBuf { private Guard _e0; }

[NonCopyable]
public struct Session : IDisposable
{
    private Guard _g;
    private GuardBuf _buf;
    private readonly Guard _ro;

    public Session(Guard g) { _g = g; _ro = default; }               // I01 SCA1001 today (by-value param), 3.8
    public readonly bool IsHeld => _g.IsHeld;                        // I02 OK
    public readonly void Close() => _g.Dispose();                    // I03 SCA1006 expected
    public void Dispose() { _g.Dispose(); foreach (ref Guard x in _buf) x.Dispose(); } // I04 OK
    public void Touch() => _ro.Touch();                              // I05 SCA1006 expected
    public ref Guard First => ref _buf[0];                           // I06 OK
    public readonly bool Any() { foreach (ref readonly Guard x in _buf) if (x.IsHeld) return true; return false; } // I07 OK (readonly getter)
    public readonly int Count() { int n = 0; for (int i = 0; i < 4; i++) if (_buf[i].IsHeld) n++; return n; } // I08 OK
}

public sealed class Pool<[NonCopyable] T>
{
    private T[] _items = new T[16];
    public Span<T> AsSpan() => _items;                               // J01 FP? SCA1031 on a span
    public Memory<T> AsMemory() => _items;                           // J02 FP?
    public ReadOnlySpan<T> AsReadOnlySpan() => _items;               // J03 FP?
    public T[] RawArray() => _items;                                 // J04 by design
    public Span<T> Items => _items;                                  // J05 OK property
    public ref T this[int i] => ref _items[i];
}

class Low1
{
    private Guard _g;
    private Guard[] _arr = new Guard[4];
    private GuardBuf _buf;
    private SpinLock _spin = new(false);
    private Lock _lock = new();

    unsafe void Use()
    {
        // arrays and spans
        ref Guard a = ref _arr[0];                                   // K01 OK
        _arr[1] = Owner.Enter();                                     // K02 OK
        _arr[2].Dispose();                                           // K03 OK
        Span<Guard> s = _arr;                                        // K04 OK
        s[0].Touch();                                                // K05 OK
        foreach (ref Guard x in s) x.Dispose();                      // K06 OK
        Span<Guard> part = s.Slice(1, 2);                            // K07 OK
        Span<Guard> part2 = s[1..3];                                 // K08 OK
        ref Guard last = ref s[^1];                                  // K09 OK
        s.Clear();                                                   // K10 OK? (Clear writes defaults)
        Guard[] made = new Guard[] { Owner.Enter(), Owner.Enter() }; // K11 OK
        Guard[] made2 = [Owner.Enter(), Owner.Enter()];              // K12 OK
        Span<Guard> stack = [Owner.Enter(), Owner.Enter()];          // K13 OK
        // inline arrays
        _buf[0] = Owner.Enter();                                     // K14 OK
        _buf[1].Dispose();                                           // K15 OK
        Span<Guard> bs = _buf;                                       // K16 OK
        foreach (ref Guard x in _buf) x.Touch();                     // K17 OK
        var local = new GuardBuf();                                  // K18 OK
        local[0] = Owner.Enter();                                    // K19 OK
        Guard copy = _buf[0];                                        // K20 SCA1001
        // Unsafe / MemoryMarshal / Marshal
        ref Guard r0 = ref MemoryMarshal.GetArrayDataReference(_arr);// K21 OK
        ref Guard r1 = ref Unsafe.Add(ref r0, 1);                    // K22 OK
        ref Guard r2 = ref MemoryMarshal.GetReference(s);            // K23 OK
        Span<Guard> one = MemoryMarshal.CreateSpan(ref _g, 1);       // K24 OK
        Span<byte> bytes = MemoryMarshal.AsBytes(s);                 // K25 ? (Guard unmanaged)
        ref Guard r3 = ref Unsafe.AsRef(in _g);                      // K26 OK
        bool same = Unsafe.AreSame(ref r0, ref r1);                  // K27 OK
        Unsafe.SkipInit(out Guard skipped);                          // K28 OK
        ref Guard r4 = ref Unsafe.NullRef<Guard>();                  // K29 OK
        int size = Unsafe.SizeOf<Guard>();                           // K30 OK
        int msize = Marshal.SizeOf<Guard>();                         // K31 ?
        fixed (Guard* p = &_g) { p->Touch(); }                       // K32 OK
        Guard* pp = (Guard*)Unsafe.AsPointer(ref _g);                // K33 OK
        ref Guard r5 = ref CollectionsMarshal.AsSpan(new List<Guard>())[0]; // K34 OK
        // ArrayPool
        Guard[] rented = ArrayPool<Guard>.Shared.Rent(8);            // K35 OK
        rented[0] = Owner.Enter();                                   // K36 OK
        ArrayPool<Guard>.Shared.Return(rented, clearArray: true);    // K37 OK
        // Interlocked/Volatile on fields of guards? (struct, not allowed) - on Task fields:
        // locks
        _spin.Enter(ref Unsafe.AsRef(false));                        // K38 ?
        bool taken = false; _spin.Enter(ref taken); _spin.Exit();    // K39 OK
        using (_lock.EnterScope()) { }                               // K40 OK
        lock (_lock) { }                                             // K41 OK
        var scope = _lock.EnterScope(); scope.Dispose();             // K42 OK
        // pool generics
        var pool = new Pool<Guard>();
        Span<Guard> ps = pool.AsSpan();                              // K43 OK
        ps[0].Dispose();                                             // K44 OK
        Guard bad = pool.AsSpan()[0];                                // K45 SCA1001
        Guard bad2 = pool.Items[0];                                  // K46 SCA1001
        // stackalloc of an unmanaged non-copyable
        Span<Guard> st = stackalloc Guard[2];                        // K47 OK
        Span<Guard> st2 = stackalloc Guard[] { Owner.Enter(), Owner.Enter() }; // K48 OK
        Span<Guard> st3 = stackalloc Guard[] { _g };                 // K49 SCA1001/3
        // Sort / Reverse etc
        s.Reverse();                                                 // K50 ? moves inside
        MemoryExtensions.Sort(s, (x, y) => 0);                       // K51 ? copies to comparison - should report
        s.Fill(default);                                             // K52 reported (never listed)
        s.CopyTo(_arr);                                              // K53 reported
        Array.Clear(_arr);                                           // K54 OK?
        Array.Resize(ref _arr, 8);                                   // K55 ? moves
        int len = _arr.Length;                                       // K56 OK
    }
}
