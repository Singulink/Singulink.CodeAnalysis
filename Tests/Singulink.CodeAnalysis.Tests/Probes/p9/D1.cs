using System;
using System.Reflection;
using Singulink.CodeAnalysis;

// [MovedFrom] path check (3.6) near the field-of-this relaxation, and a null 'this'.
public sealed class D1
{
    public Guard Held, Other;
    private int _state, _n = -1;
    private Index _idx;
    private D1? _next;

    public void M01([MovedFrom] ref Guard p) { Held = p; _state = 1; p = default; }               // M01 field of this between read and assignment: must be reported
    public void M02([MovedFrom] ref Guard p) { Guard x = p; _state = _state + 1; p = default; Held = x; } // M02 read into a local, then a field: must be reported
    public void M03([MovedFrom] ref Guard p) { Held = p; Other = Held; p = default; }               // M03
    public void M04([MovedFrom] ref Guard p) { Held = p; int n = -1; Index i = ^n; p = default; }   // M04 ^n between the read and the assignment (n a local)
    public static void M05([MovedFrom] ref Guard p, out Index i) { Sink.Held = p; int n = -1; i = default; Index j = ^n; p = default; } // M05 static, ^n
    public void M06([MovedFrom] ref Guard p) { Held = p; p = default; _state = 1; }                // M06 after the assignment: fine

    // a null 'this' through an open delegate
    public void Commit(Lock l)
    {
        Guard g = l.Enter();
        try { Sink.Held = g; _state = 1; }      // N01 a field of 'this' after the commit
        catch { g.Dispose(); throw; }
    }

    public static Lock RunM04() { var d = new D1(); var l = new Lock(); Guard g = l.Enter(); try { d.M04(ref g); } catch { } g.Dispose(); d.Held.Dispose(); return l; }
    public static Lock RunM05() { var l = new Lock(); Sink.Held = default; Guard g = l.Enter(); try { M05(ref g, out _); } catch { } g.Dispose(); Sink.Held.Dispose(); return l; }
    public static Lock RunN01()
    {
        var l = new Lock();
        Sink.Held = default;
        var commit = (Action<Lock>)Delegate.CreateDelegate(typeof(Action<Lock>), null, typeof(D1).GetMethod(nameof(Commit))!);
        try { commit(l); } catch { }
        Sink.Held.Dispose();
        return l;
    }
}

public static class RTD1
{
    public static Lock M04() => D1.RunM04();
    public static Lock M05() => D1.RunM05();
    public static Lock N01x() => D1.RunN01();
}
