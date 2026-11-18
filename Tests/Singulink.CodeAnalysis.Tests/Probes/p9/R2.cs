using System;
using Singulink.CodeAnalysis;

public sealed class R2
{
    public static Guard Held;
    private Guard _a, _b;
    static void Work() { }
    static readonly Exception s_ex = new InvalidOperationException();

    public static void T01(Lock l) { Guard g = l.Enter(); var ex = new InvalidOperationException(); try { Work(); Held = g; throw ex; } catch { g.Dispose(); } }   // T01 throws a local after the store
    public static void T02(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; throw null!; } catch { g.Dispose(); } }                                          // T02 throw null
    public static void T03(Lock l, Exception ex) { Guard g = l.Enter(); try { Work(); Held = g; throw ex; } catch { g.Dispose(); } }                               // T03 throw a parameter
    public static void T04(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; throw s_ex; } catch { g.Dispose(); } }                                           // T04 throw a static field
    public static void T05(Lock l, bool c) { Guard g = l.Enter(); var ex = new Exception(); try { Work(); Held = g; if (c) throw ex; } catch { g.Dispose(); } }     // T05 conditional throw
    public static void T06(Lock l) { Guard g = l.Enter(); var ex = new Exception(); try { Work(); Held = g; _ = ex ?? throw ex; } catch { g.Dispose(); } }         // T06 throw expression
    public static void T07(Lock l) { Guard g = l.Enter(); var ex = new Exception(); try { Work(); Held = g; throw ex; } finally { g.Dispose(); } }                 // T07 finally: reported
    public static void T08(Lock l) { Guard g = l.Enter(); var ex = new Exception(); try { Work(); Held = g; throw ex; } catch (InvalidOperationException) { } catch { g.Dispose(); } } // T08

    // two commits at the end of a try
    public void K01(Lock l) { Guard a = l.Enter(); Guard b = l.Enter(); try { Work(); _a = a; _b = b; } catch { a.Dispose(); b.Dispose(); throw; } }   // K01 instance fields
    public static void K02(Lock l) { Guard a = l.Enter(); int n = 0; try { Work(); Held = a; n++; } catch { a.Dispose(); throw; } }                      // K02 local arithmetic after
    public void K03(Lock l) { Guard a = l.Enter(); try { Work(); _a = a; _state = 1; } catch { a.Dispose(); throw; } }                                     // K03 state field after
    private int _state;

    public static Lock RunT01() { var l = new Lock(); try { T01(l); } catch { } Held.Dispose(); return l; }
    public static Lock RunT02() { var l = new Lock(); try { T02(l); } catch { } Held.Dispose(); return l; }
    public static Lock RunT03() { var l = new Lock(); try { T03(l, new Exception()); } catch { } Held.Dispose(); return l; }
    public static Lock RunT05() { var l = new Lock(); try { T05(l, true); } catch { } Held.Dispose(); return l; }
    public static Lock RunT06() { var l = new Lock(); try { T06(l); } catch { } Held.Dispose(); return l; }
}

public static class RTR2
{
    public static Lock T01() => R2.RunT01();
    public static Lock T02() => R2.RunT02();
    public static Lock T03() => R2.RunT03();
    public static Lock T05() => R2.RunT05();
    public static Lock T06() => R2.RunT06();
}
