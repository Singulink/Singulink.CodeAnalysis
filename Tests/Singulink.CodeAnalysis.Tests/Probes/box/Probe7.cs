using System;
using System.Collections;
using Singulink.CodeAnalysis;

internal abstract class PendingBase
{
    public abstract bool IsDone { get; }
}

internal interface IUntyped
{
    object Get();
}

internal sealed class Pending<[NonCopyable] T> : PendingBase, IUntyped, IEnumerable where T : struct
{
    private T _value;
    private T[] _items = new T[4];

    public override bool IsDone => true;

    public ref T Value => ref _value;

    object IUntyped.Get() => _value;                              // H01 hands out a boxed copy
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();   // H02 untyped enumerator over the elements
}

internal sealed class Unmarked<T> : PendingBase
{
    public override bool IsDone => true;
}

internal class Probe7
{
    private static void Wait(PendingBase pending) { }

    private void Use(Pending<Guard> marked, Unmarked<Guard> unmarked)
    {
        PendingBase b1 = marked;                                  // H03 own type, T marked
        Wait(marked);                                             // H04
        object o1 = marked;                                       // H05
        PendingBase b2 = unmarked;                                // H06 own type, T not marked
        var back = (Pending<Guard>)b1;                            // H07
        Guard copy = ((Pending<Guard>)GetBase()).Value;           // H08 by-value read after a downcast
    }

    private static PendingBase GetBase() => null!;
}
