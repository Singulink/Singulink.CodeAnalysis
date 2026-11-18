using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

internal class Probe13
{
    private static List<Guard> CreateGuards() => new();
    private static ValueTask<Guard> EnterAsync() => default;

    private void Go()
    {
        IEnumerator e = CreateGuards().GetEnumerator();     // V01 a new List<Guard>.Enumerator moved into a box
        object copy1 = e.Current!;                          // V02 a boxed copy of a guard
        object t = (Owner.Enter(), 1);                      // V03 a new tuple moved into a box
        object copy2 = ((ITuple)t)[0]!;                     // V04 a boxed copy of a guard
        ITuple direct = (Owner.Enter(), 1);                 // V05
        object vt = EnterAsync();                           // V06 a new ValueTask<Guard> moved into a box
        IEnumerator<Guard> typed = CreateGuards().GetEnumerator();   // V07 the element type stays visible
        IDisposable d = CreateGuards().GetEnumerator();     // V08
        object kv = new KeyValuePair<int, Guard>(1, Owner.Enter());  // V09
    }
}
