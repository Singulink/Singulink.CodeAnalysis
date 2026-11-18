using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Singulink.CodeAnalysis;

public sealed class RefList<[NonCopyable] T>
{
    private T[] _items = new T[4];
    private int _count;
    public int Count => _count;
    public ref T this[int index] { get { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(); return ref _items[index]; } }  // V01
    public void Add(T item)
    {
        if (_count == _items.Length) Grow();
        _items[_count++] = NonCopyable.Move(ref item);           // V02
    }
    private void Grow()
    {
        var next = new T[_items.Length * 2];
        for (int i = 0; i < _count; i++) next[i] = NonCopyable.Move(ref _items[i]);   // V03
        _items = next;                                          // V04
    }
    public T RemoveAt(int index)
    {
        T removed = NonCopyable.Move(ref _items[index]);         // V05
        for (int i = index; i < _count - 1; i++) _items[i] = NonCopyable.Move(ref _items[i + 1]);  // V06
        _count--;
        return removed;                                         // V07
    }
    public void Clear() { Array.Clear(_items, 0, _count); _count = 0; }   // V08
    public void Clear2() { _items.AsSpan(0, _count).Clear(); _count = 0; } // V09
    public Span<T> AsSpan() => _items.AsSpan(0, _count);         // V10 FP?
    public Enumerator GetEnumerator() => new(this);              // V11
    public ref struct Enumerator
    {
        private readonly RefList<T> _list; private int _i;
        internal Enumerator(RefList<T> list) { _list = list; _i = -1; }
        public bool MoveNext() => ++_i < _list._count;
        public ref T Current => ref _list._items[_i];            // V12
    }
    public int IndexOf<TKey>(TKey key, Func<T, TKey, bool> match) { for (int i = 0; i < _count; i++) if (match(_items[i], key)) return i; return -1; } // V13 by-value delegate param: copy
    public int IndexOf2(Predicate<T> match) => -1;
    public bool Any(RefPredicate match) { for (int i = 0; i < _count; i++) if (match(ref _items[i])) return true; return false; } // V14 OK
    public delegate bool RefPredicate(ref T item);
}

class Coll2User
{
    private readonly RefList<Guard> _l = new();
    void M()
    {
        _l.Add(Owner.Enter());                                   // V20
        foreach (ref Guard g in _l) g.Touch();                   // V21
        ref Guard first = ref _l[0];                             // V22
        Guard removed = _l.RemoveAt(0);                          // V23
        _l.Any((ref Guard g) => g.IsHeld);                       // V24
        foreach (Guard g in _l) { }                              // V25 SCA1001
    }
}
