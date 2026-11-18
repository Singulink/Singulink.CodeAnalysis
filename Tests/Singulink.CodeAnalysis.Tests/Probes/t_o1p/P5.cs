using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class KBag<[NonCopyable] T>
{
    private T[] _items = new T[4];
    private int _count;
    public void Add(T item) => _items[_count++] = item;
    public ref T Slot(int i) => ref _items[i];
}

public static class Box<[NonCopyable] T>
{
    public static T? Value;
    public static void Keep(T x) => Value = x;
}

public static class RT5
{
    static List<Guard> Make(Lock l, KBag<List<Guard>> bag)
    {
        var list = new List<Guard>();
        list.Add(l.Enter());
        bag.Add(list);                                                  // K01
        return list;                                                    // K02
    }

    public static Lock K01()
    {
        var l = new Lock();
        var bag = new KBag<List<Guard>>();
        foreach (Guard g in Make(l, bag)) g.Dispose();                  // K03
        CollectionsMarshal.AsSpan(bag.Slot(0))[0].Dispose();            // K04
        return l;
    }

    static Guard[] Make2(Lock l)
    {
        var a = new Guard[1];
        a[0] = l.Enter();
        Box<Guard[]>.Keep(a);                                           // K05
        return a;                                                       // K06
    }

    public static Lock K05()
    {
        var l = new Lock();
        IReadOnlyList<Guard> r = Make2(l);
        Guard g = r[0];                                                 // K07
        g.Dispose();
        Box<Guard[]>.Value![0].Dispose();                               // K08
        return l;
    }

    static Guard[] Make3(Lock l)
    {
        var a = new Guard[1];
        a[0] = l.Enter();
        Sink.Echo(a);                                                   // K09 marked generic method, result dropped
        return a;
    }
}
