using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Ring<[NonCopyable] T>
{
    private readonly T[] _items;
    private Ring(int n) => _items = new T[n];
    public static Ring<T> Create(int capacity) => new Ring<T>(capacity);
    public void Put(int i, T v) => _items[i] = NonCopyable.Move(ref v);
    public T Take(int i) => NonCopyable.Move(ref _items[i]);
    public List<Guard> Snapshot() => new List<Guard>();
}

public static class Fp2
{
    private static readonly Ring<Task<Guard>> s_ring = Ring<Task<Guard>>.Create(4);

    public static Ring<Task<Guard>> Make()
    {
        var r = Ring<Task<Guard>>.Create(4);                           // P01 static factory
        r.Put(0, Owner.EnterTaskAsync());
        return r;                                                      // P02
    }

    public static async Task Use()
    {
        Guard a = await Ring<Task<Guard>>.Create(1).Take(0);           // P03 default task, fine to report? 
        foreach (Guard g in s_ring.Snapshot()) { }                     // P04 Snapshot owns its list (body checked)
    }
}
