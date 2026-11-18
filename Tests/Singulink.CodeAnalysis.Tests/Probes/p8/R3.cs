using System;
using Singulink.CodeAnalysis;

public static class R3
{
    public static Guard Held;
    static void Fail() => throw new InvalidOperationException();

    public static void U01(Lock l) { Guard g = l.Enter(); try { try { Fail(); } catch { Held = g; throw; } } catch { g.Dispose(); } }      // U01 rethrow after store, outer catch
    public static void U02(Lock l) { Guard g = l.Enter(); try { try { Fail(); } catch { Held = g; throw; } } finally { g.Dispose(); } }    // U02 rethrow, outer finally
    public static void U03(Lock l) { Guard g = l.Enter(); try { Fail(); } catch (Exception ex) { Held = g; throw ex; } finally { g.Dispose(); } } // U03 store in catch, throw, same try's finally
    public static void U04(Lock l) { Guard g = l.Enter(); var ex = new Exception(); try { Held = g; throw ex; } finally { g.Dispose(); } }   // U04 = T07
    public static void U05(Lock l) { Guard g = l.Enter(); try { Fail(); } catch { Held = g; throw; } finally { g.Dispose(); } }             // U05 catch store, rethrow, finally

    public static Lock RunU01() { var l = new Lock(); try { U01(l); } catch { } Held.Dispose(); return l; }
    public static Lock RunU02() { var l = new Lock(); try { U02(l); } catch { } Held.Dispose(); return l; }
    public static Lock RunU03() { var l = new Lock(); try { U03(l); } catch { } Held.Dispose(); return l; }
    public static Lock RunU04() { var l = new Lock(); try { U04(l); } catch { } Held.Dispose(); return l; }
    public static Lock RunU05() { var l = new Lock(); try { U05(l); } catch { } Held.Dispose(); return l; }
}

public static class RTR3
{
    public static Lock U01() => R3.RunU01();
    public static Lock U02() => R3.RunU02();
    public static Lock U03() => R3.RunU03();
    public static Lock U04() => R3.RunU04();
    public static Lock U05() => R3.RunU05();
}
