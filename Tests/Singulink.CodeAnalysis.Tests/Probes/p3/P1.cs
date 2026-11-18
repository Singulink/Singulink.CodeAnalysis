using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

static class Gen1
{
    public static T Id<[NonCopyable] T>(T x) => x;
    public static object First<[NonCopyable] T>(ref T items) where T : IEnumerable { foreach (object o in items) return o; return null!; } // A10 body
    public static object? First2<[NonCopyable] T>(ref T items) where T : IList => items[0];            // A11 body
    public static IEnumerator Enum<[NonCopyable] T>(ref T items) where T : IEnumerable => items.GetEnumerator();  // A12 body
    public static object Box<[NonCopyable] T>(ref T items) => NonCopyable.Move(ref items)!;           // A13 body (known hole)
}

sealed class Stack1<[NonCopyable] T>
{
    private T[] _items = new T[4];
    private int _n;
    public void Push(T item) => _items[_n++] = NonCopyable.Move(ref item);
    public T Pop() => NonCopyable.Move(ref _items[--_n]);
    public ref T Slot(int i) => ref _items[i];
    public T this[int i] { set => _items[i] = NonCopyable.Move(ref value); }
}

class P1
{
    private Task<Guard> _t = null!;
    private List<Guard> _l = new();

    async Task M()
    {
        Task<Guard> stored = Owner.EnterTaskAsync();
        Guard a = await Gen1.Id(stored);   // A01 hole?: Id returns its by-value parameter
        Guard b = await Gen1.Id(stored);   // A02

        var s = new Stack1<Task<Guard>>();
        s.Push(_t);                        // A03 stored container into marked T by value
        s.Push(_t);                        // A04
        Guard c = await s.Pop();           // A05
        Guard d = await s.Pop();           // A06
        s.Slot(0) = _t;                    // A07 through ref T
        s[1] = _t;                         // A08 through a setter
        var s2 = new Stack1<List<Guard>>();
        s2.Push(_l);                       // A09
        Guard e = s2.Pop()[0];             // A14

        object f = Gen1.First(ref _l);     // A15 boxed copy of guard
        object? g = Gen1.First2(ref _l);   // A16
        IEnumerator h = Gen1.Enum(ref _l); // A17
        object i = Gen1.Box(ref _l);       // A18
    }
}
