using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Singulink.CodeAnalysis;

public static class Mv2
{
    // 3.6 design: two [MovedFrom] parameters read in one statement, assigned in the next
    public static (Guard, Guard) TakeBoth([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
    {
        var r = (a, b);                      // M01
        (a, b) = (default, default);         // M02
        return r;
    }

    // 3.6 design: a move of a plain ref parameter inside the reading statement
    public static (Guard, Guard) TakeWith([MovedFrom] ref Guard v, ref Guard w)
    {
        var r = (v, NonCopyable.Move(ref w)); // M03
        v = default;
        return r;
    }

    public static void Call()
    {
        Guard g = Owner.Enter();
        var (x, y) = TakeBoth(ref g, ref g);  // M04 legal C#: two guards
        Guard h = Owner.Enter();
        var (p, q) = TakeWith(ref h, ref h);  // M05
    }

    // 3.8 design: a by-value argument gives a view of the container's storage
    public static void LastUse()
    {
        var list = new List<Guard> { Owner.Enter() };
        Span<Guard> s = CollectionsMarshal.AsSpan(list);   // M06 list stays the method's own
        Guard copy = list[0];                              // M07 last use of 'list'; reported today, allowed under 3.8 as written
        s[0].Dispose();                                    // M08 the original, still live
    }
}
