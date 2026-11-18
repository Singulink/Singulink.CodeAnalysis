using System;
using System.Collections;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

internal abstract class ItemsBase
{
    public abstract object GetItems();
    public abstract IEnumerable GetSequence();
}

internal interface IUntypedItems
{
    object Get();
    object GetItems();
}

// a) covariant returns
internal sealed class Marked<[NonCopyable] T> : ItemsBase where T : struct
{
    private T[] _items = new T[4];

    public override T[] GetItems() => _items;                    // J01 covariant return: T[] behind object
    public override List<T> GetSequence() => new();              // J02 covariant return: List<T> behind IEnumerable
}

internal sealed class Plain : ItemsBase
{
    private Guard[] _items = new Guard[4];

    public override Guard[] GetItems() => _items;                // J03 same, in non-generic code
    public override Guard[] GetSequence() => _items;             // J04
}

// b) implicitly implemented interface members
internal sealed class Implicit<[NonCopyable] T> : IUntypedItems where T : struct
{
    private T _value;
    private T[] _items = new T[4];

    public object Get() => _value;                               // J05 implicit implementation, boxes
    public object GetItems() => _items;                          // J06 implicit implementation, erases
}

internal class UncheckedBase<T> where T : struct
{
    protected T Stored;
    protected T[] Items = new T[4];

    public object Get() => Stored;                               // unchecked body: T is not marked here
    public object GetItems() => Items;
}

internal sealed class Inherits<[NonCopyable] T> : UncheckedBase<T>, IUntypedItems where T : struct   // J07 the base implements the interface
{
}

internal class Probe8
{
    private static Guard[] MakeGuards() => new Guard[1];

    private void Use(ItemsBase items, Marked<Guard> marked, Inherits<Guard> inherits, IEnumerable<Guard[]> arrays, Func<Guard[]> make)
    {
        object copy1 = ((IList)items.GetItems())[0]!;            // J08 nothing here can be seen
        IUntypedItems u = inherits;                              // J09
        object copy2 = u.Get();                                  // J10 nothing here can be seen
        Func<object> f1 = MakeGuards;                            // J11 method group, covariant return
        Func<object> f2 = make;                                  // J12 delegate variance
        IEnumerable<object> e1 = arrays;                         // J13 interface variance
        Func<IList> f3 = () => MakeGuards();                     // J14 lambda return conversion
    }
}
