using System.Buffers;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public static class RT9
{
    static Guard[] Make(Lock l)
    {
        var a = new Guard[16];
        a[0] = l.Enter();
        ArrayPool<Guard>.Shared.Return(a);                              // L01
        return a;                                                       // L02
    }

    public static Lock L01()
    {
        var l = new Lock();
        IReadOnlyList<Guard> mine = Make(l);
        Guard g = mine[0];                                              // L03
        g.Dispose();
        ArrayPool<Guard>.Shared.Rent(16)[0].Dispose();                  // L04 the pool hands the same array to the next caller
        return l;
    }
}
