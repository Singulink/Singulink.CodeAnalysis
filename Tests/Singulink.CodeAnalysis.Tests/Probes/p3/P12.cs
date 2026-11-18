using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Singulink.CodeAnalysis;

interface IUntyped12 { object? Get(); }
sealed class Cell12<[NonCopyable] T> : IUntyped12
{
    private T _v = default!;
    public void Put(T v) => _v = NonCopyable.Move(ref v);
    public object? Get() => NonCopyable.Move(ref _v);                 // N00 (known: boxes a new T)
}

sealed class Outer12<[NonCopyable] U>
{
    public object Wrap(U value)
    {
        var cell = new Cell12<U>();
        cell.Put(NonCopyable.Move(ref value));
        return cell;                                                   // N01 Cell12<U> to object: allowed inside generic code
    }
}

sealed class Pending12<[NonCopyable] T> : IUntyped12 where T : struct
{
    private T _v;
    public void Put(T v) => _v = NonCopyable.Move(ref v);
    public object? Get() => NonCopyable.Move(ref _v);                  // N02 struct-constrained: allowed
}

class P12
{
    void M(List<Guard> list)
    {
        object o = new Outer12<List<Guard>>().Wrap(list);              // N03 nothing at the call site?
        var copy = ((IList)((IUntyped12)o).Get()!)[0];                 // N04 boxed copy of a guard

        var p = new Pending12<(Guard, int)>();                         // N05 erasable (a tuple is a non-copyable value)
        p.Put((Owner.Enter(), 1));
        object po = p;                                                 // N06 allowed
        var copy2 = ((ITuple)((IUntyped12)po).Get()!)[0];              // N07 boxed copy of a guard
    }
}
