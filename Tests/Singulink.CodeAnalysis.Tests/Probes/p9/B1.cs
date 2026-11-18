using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// How the block that commits can end or be left.
public sealed class B1
{
    public static Guard Held, Held2;
    public Guard F;
    private static readonly object s_o = new();
    private static bool s_fail;
    private static int s_i;
    private static List<int> s_list = [1];
    static void Work() { if (s_fail) throw new InvalidOperationException(); }
    static void Log() { }
    static async Task WorkAsync() { await Task.Yield(); Work(); }
    static IDisposable Res() => new Res0();
    sealed class Res0 : IDisposable { public void Dispose() { if (s_fail2) throw new InvalidOperationException("dispose"); } }
    static bool s_fail2;

    public static void B01(Lock l) { Guard g = l.Enter(); try { try { Work(); Held = g; return; } finally { Log(); } } catch { g.Dispose(); throw; } }     // B01 return through an inner finally
    public static void B02(Lock l) { Guard g = l.Enter(); try { while (true) { Work(); Held = g; break; } } catch { g.Dispose(); throw; } }              // B02 break
    public static void B03(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; goto done; } catch { g.Dispose(); throw; } done:; }                  // B03 goto out
    public static void B04(Lock l) { Guard g = l.Enter(); try { foreach (var x in s_list) { Work(); Held = g; break; } } catch { g.Dispose(); throw; } } // B04 break out of foreach (Dispose)
    public static void B05(Lock l) { Guard g = l.Enter(); try { using (Res()) { Work(); Held = g; } } catch { g.Dispose(); throw; } }                 // B05 leave a using
    public static void B06(Lock l) { Guard g = l.Enter(); try { lock (s_o) { Work(); Held = g; } } catch { g.Dispose(); throw; } }                     // B06 leave a lock
    public static void B07(Lock l) { Guard g = l.Enter(); try { checked { Work(); Held = g; } } catch { g.Dispose(); throw; } }                         // B07 leave a checked block
    public static unsafe void B08(Lock l, int[] a) { Guard g = l.Enter(); try { fixed (int* p = a) { Work(); Held = g; } } catch { g.Dispose(); throw; } } // B08 leave a fixed
    public static void B09(Lock l, int k) { Guard g = l.Enter(); try { switch (k) { case 1: Work(); Held = g; break; default: Held2 = g; break; } } catch { g.Dispose(); throw; } } // B09 switch sections
    public static void B10(Lock l, int k) { Guard g = l.Enter(); try { switch (k) { case 1: Work(); Held = g; goto case 2; case 2: Log(); break; } } catch { g.Dispose(); throw; } } // B10 goto case
    public static async Task B11(Lock l) { Guard g = l.Enter(); try { await WorkAsync(); Held = g; } catch { g.Dispose(); throw; } }                  // B11 async, store after an await
    public static async Task B12(Lock l) { Guard g = l.Enter(); try { await WorkAsync(); Held = g; await Task.Yield(); } catch { g.Dispose(); throw; } } // B12 await after the store
    public static async Task B13(Lock l) { Guard g = l.Enter(); try { await using (new AsyncRes()) { Held = g; } } catch { g.Dispose(); throw; } }     // B13 await using
    public static IEnumerable<int> B14(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; } catch { g.Dispose(); throw; } yield break; }            // B14 iterator
    public static IEnumerable<int> B15(Lock l) { Guard g = l.Enter(); try { try { Work(); Held = g; yield break; } finally { Log(); } } finally { } }   // B15 yield break through finally
    public static void B16(Lock l) { Guard g = l.Enter(); try { Work(); } catch { try { Work(); Held = g; } catch { g.Dispose(); throw; } } }           // B16 try inside a catch
    public static void B17(Lock l) { Guard g = l.Enter(); try { try { Work(); } finally { try { Held = g; } catch { } } } catch { g.Dispose(); throw; } } // B17 try in a finally, handler outside
    public static void B18(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch { Held = g; } } catch { g.Dispose(); throw; } }                 // B18 commit in a catch, sound
    public static void B19(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch { Held = g; } finally { Log(); } } catch { g.Dispose(); throw; } } // B19 catch then a finally
    public static void B20(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; } catch { } g.Dispose(); }                                           // B20 swallowed, used after
    public static void B21(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; } catch when (Filter(ref g)) { } }                                  // B21 filter uses g by ref
    public static void B22(Lock l) { Guard g = l.Enter(); try { try { Work(); Held = g; } catch when (s_i > 0) { } } catch { g.Dispose(); throw; } }  // B22 filter that may give false
    public static void B23(Lock l) { Guard g = l.Enter(); try { try { Work(); } finally { Held = g; if (s_i > 0) Log(); } } catch { g.Dispose(); throw; } } // B23 store in a finally, not its last block
    public static void B24(Lock l) { Guard g = l.Enter(); try { Work(); Action a = () => Log(); Held = g; } catch { g.Dispose(); throw; } }            // B24 lambda before
    public static void B25(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; void Local() { Log(); } } catch { g.Dispose(); throw; } }            // B25 local function declared after
    public static void B26(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; } catch { g.Dispose(); throw; } finally { Log(); } }               // B26 catch and finally that does not use g
    public static void B27(Lock l) { Guard g = l.Enter(); try { try { Work(); Held = g; } finally { Log(); } } catch { g.Dispose(); throw; } }      // B27 inner finally that can throw
    public static void B28(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch (InvalidOperationException) { Held = g; } } catch { g.Dispose(); throw; } } // B28
    public static void B29(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch { Held = g; goto end; } end: Log(); } catch { g.Dispose(); throw; } } // B29 goto in try
    public static void B30(Lock l) { Guard g = l.Enter(); try { try { Work(); } finally { try { Held = g; } finally { } } } catch { g.Dispose(); throw; } } // B30 try/finally in a finally
    public static void B31(Lock l) { Guard g = l.Enter(); try { try { Work(); } finally { try { Held = g; } catch { } finally { } } } catch { g.Dispose(); throw; } } // B31
    public static void B32(Lock l) { Guard g = l.Enter(); try { try { Work(); } finally { if (s_i == 0) { try { Held = g; } catch { } } } } catch { g.Dispose(); throw; } } // B32
    public static void B33(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch { try { Held = g; } catch { } throw; } } catch { g.Dispose(); throw; } } // B33 rethrow after a nested try in a catch
    public static void B34(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch { try { Held = g; } finally { } throw; } } catch { g.Dispose(); throw; } } // B34
    public static void B35(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch when (Keep(g.IsHeld)) { } } catch { } }                           // B35 (filter use is read in place)
    public static void B36(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch { try { Held = g; } catch { } } } catch { g.Dispose(); throw; } } // B36 nested try in a catch, sound

    static bool Filter(ref Guard g) { g.Dispose(); return false; }
    static bool Keep(bool b) => b;

    public static Lock Run(Action<Lock> m, bool fail = true) { s_fail = fail; var l = new Lock(); Held = default; try { m(l); } catch { } Held.Dispose(); s_fail = false; return l; }
}

public sealed class AsyncRes : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }

public static class RTB1
{
    public static Lock B17() => B1.Run(B1.B17);
    public static Lock B30() => B1.Run(B1.B30);
    public static Lock B31() => B1.Run(B1.B31);
    public static Lock B32() => B1.Run(B1.B32);
    public static Lock B33() => B1.Run(B1.B33);
    public static Lock B34() => B1.Run(B1.B34);
    public static Lock B23() => B1.Run(B1.B23);
    public static Lock B18() => B1.Run(B1.B18);
    public static Lock B36() => B1.Run(B1.B36);
}
