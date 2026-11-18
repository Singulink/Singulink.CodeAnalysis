using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;
using static Src;

class Pool<[NonCopyable] T> where T : struct
{
    T[] _items = new T[4];
    public ref T this[int i] => ref _items[i];
    public T Get(int i) => _items[i];                            // AA1 report
    [RequiresCopyable] public T[] ToArray() => (T[])_items.Clone();
    [RequiresCopyable] public Pool() { }
    public Pool(int n) { }
    [RequiresCopyable] public T First => _items[0];
    [RequiresCopyable] public event Action<T>? Changed;
    void Own() { var a = ToArray(); Func<T[]> f = ToArray; }     // AA2 report x2
    public T Take(int i) => NonCopyable.Move(ref _items[i]);     // AA3 OK
}
class DPool<[NonCopyable] U> : Pool<U> where U : struct { public DPool() : base(1) { } }
class BadPool<U> : Pool<U> where U : struct { public BadPool() : base(1) { } }        // AA4 report
interface IPool<[NonCopyable] T> { T[] ToArray(); }
class PoolImpl<[NonCopyable] T> : IPool<T> { [RequiresCopyable] public T[] ToArray() => default!; } // AA5 report
abstract class AB<[NonCopyable] T> { public abstract T[] Copy(); }
class AD<[NonCopyable] T> : AB<T> { [RequiresCopyable] public override T[] Copy() => default!; }   // AA6 report

class P09
{
    void M(Pool<Guard> p, Pool<int> pi)
    {
        var a = p.ToArray();                                     // AB1 report
        var b = pi.ToArray();                                    // AB2 OK
        var c = new Pool<Guard>();                               // AB3 report
        var d = new Pool<Guard>(1);                              // AB4 OK
        Guard e = p.First;                                       // AB5 report
        p.Changed += g => { };                                   // AB6 report
        var f = new DPool<Guard>().ToArray();                    // AB7 report
        Guard g2 = p.Take(0);                                    // AB8 OK
        ref Guard r = ref p[0];                                  // AB9 OK
        Func<Guard[]> h = p.ToArray;                             // AB10 report
        Guard i = p.Get(1);                                      // AB11 OK (trusted)
        Guard j = new Pool<Guard>(1).Take(0);                    // AB12 OK
    }
}
