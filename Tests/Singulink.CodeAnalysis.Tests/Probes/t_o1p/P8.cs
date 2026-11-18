using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Singulink.CodeAnalysis;

public sealed class Wrapper
{
    public static List<Guard>? Last;
    public static implicit operator Wrapper([Kept] List<Guard> l) { Last = l; return new Wrapper(); }   // K40
    public static Wrapper operator +(Wrapper w, [Kept] List<Guard> l) { Last = l; return w; }         // K45
}

[InterpolatedStringHandler]
public ref struct KeepingHandler
{
    public static List<Guard>? Last;
    public KeepingHandler(int literalLength, int formattedCount) { }
    public void AppendLiteral(string s) { }
    public void AppendFormatted([Kept] List<Guard> l) => Last = l;                                       // K41
}

public sealed class MBag<[NonCopyable] T> : IEnumerable
{
    private readonly T[] _items = new T[4];
    private int _count;
    public void Add(T item) => _items[_count++] = item;
    public ref T Slot(int i) => ref _items[i];
    IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
}

public static class RT8
{
    static List<Guard>? s_cache;
    static MBag<List<Guard>>? s_bag;
    static void Log(KeepingHandler h) { }

    [return: Borrowed]
    static List<Guard> EnsureNotEmpty(List<Guard> l) { if (l.Count == 0) throw new InvalidOperationException(); return l; }   // K42

    static List<Guard> Make(Lock l, int how, out Span<Guard> span)
    {
        var list = new List<Guard>();
        list.Add(l.Enter());
        span = default;
        switch (how)
        {
            case 40: Wrapper w = list; break;
            case 41: Log($"{list}"); break;
            case 42: s_cache = EnsureNotEmpty(list); break;
            case 43: span = CollectionsMarshal.AsSpan(list); break;
            case 44: s_bag = new MBag<List<Guard>> { list }; break;
            case 45: _ = new Wrapper() + list; break;
        }
        return list;                                                    // K46
    }

    static Lock Run(int how)
    {
        var l = new Lock();
        var list = Make(l, how, out var span);
        foreach (Guard g in list) g.Dispose();                          // K47
        switch (how)
        {
            case 40 or 45: CollectionsMarshal.AsSpan(Wrapper.Last)[0].Dispose(); break;
            case 41: CollectionsMarshal.AsSpan(KeepingHandler.Last)[0].Dispose(); break;
            case 42: CollectionsMarshal.AsSpan(s_cache)[0].Dispose(); break;
            case 43: span[0].Dispose(); break;
            case 44: CollectionsMarshal.AsSpan(s_bag!.Slot(0))[0].Dispose(); break;
        }
        return l;
    }
    public static Lock K40() => Run(40);
    public static Lock K41() => Run(41);
    public static Lock K42() => Run(42);
    public static Lock K43() => Run(43);
    public static Lock K44() => Run(44);
    public static Lock K45() => Run(45);
}
