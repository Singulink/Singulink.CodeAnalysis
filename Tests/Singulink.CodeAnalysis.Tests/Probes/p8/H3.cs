using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public struct PooledBuffer<[NonCopyable] T> : IDisposable          // A01 should be [NonCopyable] itself? (holds T[]: container, copyable)
{
    private T[] _items;
    private int _count;

    public PooledBuffer(int capacity) { _items = ArrayPool<T>.Shared.Rent(capacity); _count = 0; }   // A02

    public void Add(T item)
    {
        if (_count == _items.Length) Grow();
        _items[_count++] = item;                                                                    // A03 clean
    }

    private void Grow()
    {
        var bigger = ArrayPool<T>.Shared.Rent(_items.Length * 2);
        for (int i = 0; i < _count; i++) bigger[i] = NonCopyable.Move(ref _items[i]);               // A04 clean
        ArrayPool<T>.Shared.Return(_items, RuntimeHelpers.IsReferenceOrContainsReferences<T>());    // A05 clean
        _items = bigger;
    }

    public T RemoveLast() => NonCopyable.Move(ref _items[--_count]);                                 // A06 clean
    public readonly ref T this[int i] => ref _items[i];                                              // A07 clean
    public readonly Span<T> Span => _items.AsSpan(0, _count);                                        // A08 clean
    public T Get(int i) => _items[i];                                                                // A09 reported

    public void Dispose()
    {
        _items.AsSpan(0, _count).Clear();                                                            // A10 clean
        ArrayPool<T>.Shared.Return(_items);                                                          // A11
        _items = null!;
    }
}

public static class H3
{
    public static Guard Held;

    // copying a PooledBuffer<Guard>: the struct is copyable (it holds only an array), so a copy aliases the storage
    public static void B01(Lock l)
    {
        var buf = new PooledBuffer<Guard>(4);
        buf.Add(l.Enter());
        var copy = buf;                                     // B01 copy of the buffer struct: aliasing
        Guard a = buf.RemoveLast();
        Guard b = copy.RemoveLast();                        // B02 second guard from the same slot? (slot was reset by move)
        a.Dispose(); b.Dispose();
    }

    // a generic helper that is a move
    public static T Take<[NonCopyable] T>([MovedFrom] ref T value) { T v = value; value = default!; return v; } // C01 clean
    public static void Swap<[NonCopyable] T>(ref T a, ref T b) { T t = NonCopyable.Move(ref a); a = NonCopyable.Move(ref b); b = t; } // C02 clean
    public static void Replace<[NonCopyable] T>(ref T slot, T value) where T : IDisposable { slot.Dispose(); slot = value; } // C03 clean
    public static T Exchange<[NonCopyable] T>(ref T slot, T value) { T old = NonCopyable.Move(ref slot); slot = value; return old; } // C04 clean

    // state machine holding a guard in a field
    public sealed class Session
    {
        private Guard _guard;
        private int _state;
        public void Open(Lock l) { if (_state == 0) { _guard = l.Enter(); _state = 1; } }
        public Guard Detach() { _state = 0; return NonCopyable.Move(ref _guard); }                 // D01 clean
        public Guard DetachBad() { _state = 0; return _guard; }                                    // D02 reported
        public void Close() { _guard.Dispose(); _state = 0; }
        public ref Guard Current => ref _guard;
    }

    public static void D03(Session s) { Guard g = s.Current; g.Dispose(); }                       // D03 reported
    public static void D04(Session s) { ref Guard g = ref s.Current; g.Dispose(); }               // D04 clean

    // ValueTask helpers
    public static async ValueTask<Guard> Wrap(Lock l) { var g = await l.EnterAsync(); return g; } // E01 clean
    public static ValueTask<Guard> Fast(Lock l) => l.EnterAsync();                                 // E02 clean
    public static async Task E03(Lock l) { var vts = new List<ValueTask<Guard>>(); vts.Add(l.EnterAsync()); foreach (var vt in vts) { using var g = await vt; } } // E03 reported
    public static async Task E04(Lock l) { var vt = l.EnterAsync(); try { using var g = await vt; } catch { vt.AsTask().Dispose(); } } // E04 reported (catch reads vt)

    // copies through a struct wrapper
    [NonCopyable] public struct Holder { public Guard G; public Holder(Guard g) { G = g; } }
    public static void F01(Lock l) { Guard g = l.Enter(); var h = new Holder(g); g.Dispose(); }   // F01 reported
    public static void F02(Lock l) { var h = new Holder(l.Enter()); var h2 = h; }                  // F02 reported
    public static Holder F03(Lock l) { var h = new Holder(l.Enter()); return h; }                  // F03 clean
    public static void F04(Lock l) { var h = new Holder(l.Enter()); Held = h.G; h.G.Dispose(); }   // F04 reported

    // generic passes
    public static void G01(Lock l) { Guard g = l.Enter(); Sink.Echo(g); }                          // G01 clean (last use into marked)
    public static void G02(Lock l) { Guard g = l.Enter(); var x = Sink.Echo(g); g.Dispose(); }    // G02 reported
    static void Plain<T>(T value) { }
    public static void G03(Lock l) { Plain(l.Enter()); }                                           // G03 SCA1020
    public static void G04(Lock l) { Guard g = l.Enter(); Action<Guard> a = Plain; a(g); }        // G04 SCA1020 at method group

    // disposal in catch and after
    public static void H01(Lock l) { Guard g = l.Enter(); try { Sink.Consume(g); } catch { g.Dispose(); } } // H01 reported
    public static void H02(Lock l) { Guard g = l.Enter(); try { Sink.Release(ref g); } catch { g.Dispose(); } } // H02 clean (by ref)
}

public static class RTH3
{
    public static Lock B01() { var l = new Lock(); var buf = new PooledBuffer<Guard>(4); buf.Add(l.Enter()); var copy = buf; Guard a = buf.RemoveLast(); copy.Add(l.Enter()); Guard b = copy.RemoveLast(); a.Dispose(); b.Dispose(); return l; }
}
