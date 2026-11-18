using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Singulink.CodeAnalysis;

interface IGet { object? Get(); }
struct Wrap<T> : IGet { public T V; public object? Get() => V; }

class P2
{
    private List<Guard> _l = new();

    void M(List<Guard> list)
    {
        object box = (Owner.Enter(), 1);                       // B01 allowed: move into box
        var it = (ITuple)box;                                  // B02
        ((IDisposable)it[0]!).Dispose();                       // B03 disposes a boxed copy
        ((IDisposable)it[0]!).Dispose();                       // B04 and another
        ITuple t2 = (Owner.Enter(), 2);                        // B05
        object box3 = (Owner.Enter(), list);                   // B06 a stored list into a box
        IList il = (IList)((ITuple)box3)[1]!;                  // B07 untyped list of guards
        object? copy = il[0];                                  // B08 boxed copy
        IStructuralEquatable se = (Owner.Enter(), 3);          // B09

        Wrap<Guard> w = default;                               // B10
        w.V = Owner.Enter();                                   // B11
        object wb = NonCopyable.Move(ref w);                   // B12
        object? wc = ((IGet)wb).Get();                         // B13 copy
        IGet wd = new Wrap<Guard> { V = Owner.Enter() };       // B14

        var kv = new KeyValuePair<Guard, List<Guard>>(Owner.Enter(), list);  // B15
        object kvb = NonCopyable.Move(ref kv);                // B16
    }
}
