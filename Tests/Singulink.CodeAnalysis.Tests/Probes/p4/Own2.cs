using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// A user's generic collection with a marked type parameter.
public sealed class Bag<[NonCopyable] T>
{
    private T[] _items = new T[8];
    private int _n;

    public int Count => _n;
    public void Add(T item) => _items[_n++] = NonCopyable.Move(ref item);
    public void AddRange(Span<T> items) { foreach (ref T x in items) _items[_n++] = NonCopyable.Move(ref x); }
    public void AddArray(T[] items) { for (int i = 0; i < items.Length; i++) _items[_n++] = NonCopyable.Move(ref items[i]); }
    public void AddParams(params T[] items) { for (int i = 0; i < items.Length; i++) _items[_n++] = NonCopyable.Move(ref items[i]); }
    public void AddList(List<T> items) { for (int i = 0; i < items.Count; i++) _items[_n++] = NonCopyable.Move(ref CollectionsMarshal.AsSpan(items)[i]); }
    public void AddFrom(Bag<T> other) { while (other._n > 0) Add(other.Take()); }
    public T Take() => NonCopyable.Move(ref _items[--_n]);
    public ref T this[int i] => ref _items[i];
    public Span<T> AsSpan() => _items.AsSpan(0, _n);
    public void Swap(ref T value) { T old = NonCopyable.Move(ref _items[0]); _items[0] = NonCopyable.Move(ref value); value = NonCopyable.Move(ref old); }
    public bool TryTake(out T value) { if (_n == 0) { value = default!; return false; } value = Take(); return true; }
    public void Set(int i, in T v) { }
}

public sealed class Cell<[NonCopyable] T>
{
    public T Value = default!;
    public T Prop { set => Value = NonCopyable.Move(ref value); }
}

class Own2
{
    private Task<Guard> _t = null!;
    private Task<Guard>[] _ta = new Task<Guard>[2];
    private List<Task<Guard>> _tl = new();

    async Task Ordinary()
    {
        // ordinary use with guards: expect no warnings
        var bag = new Bag<Guard>();
        bag.Add(Owner.Enter());                     // C01 OK
        bag[0].Dispose();                           // C02 OK
        ref Guard r = ref bag[0];                   // C03 OK
        Guard g = bag.Take();                       // C04 OK
        bag.AddRange(stackalloc Guard[0]);          // C05 OK? (empty)
        Span<Guard> s = bag.AsSpan();               // C06 OK
        if (bag.TryTake(out var got)) got.Dispose();// C07 OK
        var other = new Bag<Guard>();
        other.AddFrom(bag);                         // C08 OK
        bag.Swap(ref g);                            // C09 OK
        bag.Set(0, in g);                           // C10 OK
        Guard[] arr = new Guard[2];
        bag.AddArray(arr);                          // C11 OK: moves out of the array
        bag.AddRange(arr);                          // C12 OK
        bag.AddParams(Owner.Enter(), Owner.Enter());// C13 OK
    }

    async Task Holes()
    {
        var bag = new Bag<Task<Guard>>();
        bag.Add(_t);                                // D01 SCA1033 expected
        bag.AddRange(new[] { _t, _t });             // D02 expected: something
        bag.AddRange([_t, _t]);                     // D03 expected: something
        bag.AddArray(_ta);                          // D04 expected: something (array of stored tasks)
        bag.AddParams(_t, _t);                      // D05 expected: something
        bag.AddList(_tl);                           // D06 expected: something
        Guard a = await bag.Take();                 // D07 (new by rule)
        Guard b = await bag.Take();                 // D08
        bag[0] = _t;                                // D09 SCA1033 expected
        ref var slot = ref bag[0]; slot = _t;       // D10 known gap (ref local)
        bag.AsSpan()[0] = _t;                       // D11 known gap? (span handed out)
        var t = _t; bag.Swap(ref t);                // D12 swap a stored container in via ref
        bag.Set(0, in _t);                          // D13 in T
        var cell = new Cell<Task<Guard>>();
        cell.Value = _t;                            // D14 SCA1033 expected
        cell.Prop = _t;                             // D15 SCA1033 expected
        var cell2 = new Cell<Task<Guard>> { Value = _t };  // D16 SCA1033 expected
        Guard c = await Pass(_t);                   // D17 SCA1033
        Guard d = await Pass2(new[] { _t })[0];     // D18
        Guard e = await First(_ta);                 // D19
        var (x, y) = Pair(_t);                      // D20
        Guard f = await Pair(_t).Item1;             // D21 SCA1033 expected for _t into (T,T)?
        Guard h = await Wrap(_t).Value;             // D22
        Guard i = await Unwrap(new Cell<Task<Guard>> { Value = Owner.EnterTaskAsync() }); // D23 OK
    }

    static T Pass<[NonCopyable] T>(T v) => v;
    static T[] Pass2<[NonCopyable] T>(T[] v) => v;
    static T First<[NonCopyable] T>(T[] v) => NonCopyable.Move(ref v[0]);
    static (T, T) Pair<[NonCopyable] T>(T v) => (v, default!);
    static (T, int) Pair2<[NonCopyable] T>((T, int) v) => v;
    static Cell<T> Wrap<[NonCopyable] T>(T v) => new Cell<T> { Value = NonCopyable.Move(ref v) };
    static T Unwrap<[NonCopyable] T>(Cell<T> c) => NonCopyable.Move(ref c.Value);
    async Task Holes2()
    {
        var c = new Cell<Task<Guard>> { Value = Owner.EnterTaskAsync() };
        Guard a = await Unwrap(c);                  // D24 c stored, Unwrap takes from a stored cell: Move from field -- new
        Guard b = await Unwrap(c);                  // D25 second: default -> null. fine
        Guard g = await Pair2((_t, 1)).Item1;       // D26 tuple with stored container into marked T inside a tuple
        Guard h = await First(new[] { _t });        // D27
    }
}
