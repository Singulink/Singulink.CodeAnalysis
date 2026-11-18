using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Sink;

public static class RTLU3
{
    static bool Take(Guard g) { g.Dispose(); return false; }
    static int Rel(ref Guard g) { g.Dispose(); return 0; }
    static void Two(int a, Guard b) => b.Dispose();

    // a filter that returns false passes the exception to the next catch of the same try
    public static Lock F01()
    {
        var l = new Lock(); Guard g = l.Enter();
        try { throw new Exception(); }
        catch when (Take(g)) { }                 // F01 must be reported: the next catch uses g
        catch { g.Dispose(); }
        return l;
    }

    // the same with a typed second catch
    public static Lock F02()
    {
        var l = new Lock(); Guard g = l.Enter();
        try { throw new InvalidOperationException(); }
        catch (InvalidOperationException) when (Take(g)) { }   // F02 must be reported
        catch (InvalidOperationException) { g.Dispose(); }
        return l;
    }

    // a filter that returns false and no other catch: the exception goes on to an outer catch
    public static Lock F03()
    {
        var l = new Lock(); Guard g = l.Enter();
        try
        {
            try { throw new Exception(); }
            catch when (Take(g)) { }             // F03 must be reported: the outer catch uses g
        }
        catch { g.Dispose(); }
        return l;
    }

    // a filter that returns false, then the code after the try statement of an outer catch
    public static Lock F04()
    {
        var l = new Lock(); Guard g = l.Enter();
        try
        {
            try { throw new Exception(); }
            catch when (Take(g)) { }             // F04 must be reported: after the outer catch g is used
        }
        catch { }
        g.Dispose();
        return l;
    }

    // order of named arguments: Rel first, then the read: clean is right
    public static Lock F05() { var l = new Lock(); Guard g = l.Enter(); Two(a: Rel(ref g), b: g); return l; }   // F05 clean
}
