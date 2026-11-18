using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class GuardList<[NonCopyable] T>
{
    private T[] _items = new T[4];
    private int _n;
    private readonly Func<T>? _factory;

    public GuardList() { }
    public GuardList(Func<T> factory) => _factory = factory;

    public int Count => _n;
    public ref T this[int i] => ref _items[i];

    [return: Borrowed]
    public GuardList<T> Add(T item) { _items[_n++] = NonCopyable.Move(ref item); return this; }    // Z01
    public GuardList<T> AddNew() { _items[_n++] = _factory!(); return this; }                     // Z02 no Borrowed
    public T RemoveLast() => NonCopyable.Move(ref _items[--_n]);
    public T TakeAt(int i) { T v = NonCopyable.Move(ref _items[i]); _items[i] = NonCopyable.Move(ref _items[--_n]); return v; }   // Z03

    public static T Exchange([MovedFrom] ref T slot, [MovedFrom] ref T with)                       // Z04 my own move helper (swap)
    {
        T old = slot;
        slot = with;
        with = default!;
        return old;
    }

    public static T Take([MovedFrom] ref T v) { T m = v; v = default!; return m; }                  // Z05

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly GuardList<T> _list;
        private int _i;
        public Enumerator(GuardList<T> list) { _list = list; _i = -1; }
        public bool MoveNext() => ++_i < _list._n;
        public ref T Current => ref _list._items[_i];
    }

    public Span<T> AsSpan() => _items.AsSpan(0, _n);

    public GuardList<T> Where(Func<T, bool> pred)                                                    // Z06 hmm: in-ref predicate
    {
        var r = new GuardList<T>();
        for (int i = _n - 1; i >= 0; i--) if (pred(_items[i])) r.Add(TakeAt(i));                    // Z07
        return r;
    }

    public void ForEach(Action<T> a) { }
    public static GuardList<T> Create(int n, Func<T> make)                                           // Z08
    {
        var l = new GuardList<T>();
        for (int i = 0; i < n; i++) l.Add(make());
        return l;
    }
    public static Func<GuardList<T>> Factory() => () => new GuardList<T>();                           // Z09
}

public static class GuardPool<[NonCopyable] T> where T : struct
{
    [ThreadStatic] private static T[]? s_items;
    private static int s_n;
    public static T Rent(Func<T> make) => s_n > 0 ? NonCopyable.Move(ref s_items![--s_n]) : make();   // Z10
    public static void Return(T item) { (s_items ??= new T[8])[s_n++] = NonCopyable.Move(ref item); }  // Z11
}

public sealed class UseGuardList
{
    public async Task Run()
    {
        var gl = new GuardList<Guard>(Owner.Enter);                                         // Z20
        gl.Add(Owner.Enter()).Add(Owner.Enter()).AddNew();                                  // Z21
        foreach (ref var g in gl) g.Touch();                                                // Z22
        Guard last = gl.RemoveLast();                                                       // Z23
        var a = Owner.Enter();
        var b = Owner.Enter();
        Guard old = GuardList<Guard>.Exchange(ref a, ref b);                                // Z24
        Guard r = GuardPool<Guard>.Rent(Owner.Enter);                                       // Z25
        GuardPool<Guard>.Return(NonCopyable.Move(ref r));                                   // Z26
        var created = GuardList<Guard>.Create(3, Owner.Enter);                              // Z27
        foreach (ref var g in created.AsSpan()) g.Dispose();                                // Z28

        // with tasks
        var tl = new GuardList<Task<Guard>>();
        tl.Add(Owner.EnterTaskAsync());                                                     // Z30
        var t1 = Owner.EnterTaskAsync();
        Guard g1 = await GuardList<Task<Guard>>.Take(ref t1);                               // Z31
        var t2 = Owner.EnterTaskAsync();
        var t3 = Owner.EnterTaskAsync();
        Guard g2 = await GuardList<Task<Guard>>.Exchange(ref t2, ref t3);                   // Z32
        Guard g3 = await NonCopyable.Move(ref t2);                                          // Z33
    }
}
