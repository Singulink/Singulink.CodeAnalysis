using System.Collections;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

internal sealed class Boxes<[NonCopyable] T>
{
    private T _value = default!;

    public object Get() => NonCopyable.Move(ref _value)!;       // U01 a new T moved into a box
    public object Peek() => _value!;                            // U02 a stored T boxed
}

internal class UseBoxes
{
    private void Go(Boxes<List<Guard>> boxes, Boxes<Guard> guardBoxes)
    {
        var made = new Boxes<List<Guard>>();                    // U03 a container bound to a marked type parameter
        object o = boxes.Get();                                 // U04 the list comes out as object
        object copy = ((IList)o)[0]!;                           // U05 a boxed copy of a guard
        object g = guardBoxes.Get();                            // U06 a boxed guard: fine, the way out is reported
    }
}
