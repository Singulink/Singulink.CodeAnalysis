using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

class RC9<[NonCopyable] T>
{
    private T[] _items = new T[2];
    [RequiresCopyable] public RC9() { }
    [RequiresCopyable] public IEnumerator<T> GetEnumerator() { foreach (var x in _items) yield return x; }
    [RequiresCopyable] public void Dispose() { }
    [RequiresCopyable] public void Deconstruct(out T a, out T b) { a = _items[0]; b = _items[1]; }
    [RequiresCopyable] public TaskAwaiter<T> GetAwaiter() => Task.FromResult(_items[0]).GetAwaiter();
    [RequiresCopyable] public void Add(T item) { }
    public RC9(int x) { }
}

static class Mk9
{
    public static T Make<[NonCopyable] T>() where T : new() => new T();
    public static object Box<[NonCopyable] T>(ref T v) where T : struct => NonCopyable.Move(ref v);   // struct-constrained: allowed today
}

class P9
{
    async Task M()
    {
        var a = new RC9<Guard>();                                    // K01 SCA1021
        var b = Mk9.Make<RC9<Guard>>();                              // K02 calls the [RequiresCopyable] constructor through new()
        var c = new RC9<Guard>(1);
        foreach (var x in c) { }                                     // K03 SCA1021
        var (e, f) = c;                                              // K05 SCA1021
        Guard g = await c;                                           // K06 SCA1021

        var seg = new ArraySegment<Guard>(new Guard[2]);
        object box = Mk9.Box(ref seg);                               // K10 a view in a box
        foreach (object o in (IEnumerable)box) { }                   // K11 boxed copies
        var tup = (Owner.Enter(), 1);
        object box2 = Mk9.Box(ref tup);                              // K12 a tuple in a box
        object? copy = ((ITuple)box2)[0];                            // K13 boxed copy
    }
}
