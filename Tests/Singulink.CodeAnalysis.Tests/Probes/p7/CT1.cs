using System;
using Singulink.CodeAnalysis;

public class BaseD { public BaseD(int a, int b) { } }

public sealed class D : BaseD
{
    static int Pass(Guard g, out Guard o) { o = g; return 0; }
    static int Take(Guard g) { g.Dispose(); return 0; }

    public D(Lock l) : base(Pass(l.Enter(), out Guard g), Take(g))   // J01 must be reported: the body uses g
    {
        g.Dispose();
    }

    private Guard _g;
    public Guard Prop { set { _g = value; } }   // J02 clean
}

public static class RTCT1
{
    public static Lock J03() { var l = new Lock(); _ = new D(l); return l; }
}
