using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Singulink.CodeAnalysis;

public sealed class HolderC { public Guard G; }
[NonCopyable] public struct HolderS { public Guard G; public int X; }
public sealed record HolderR { public Guard G; }

// Filters, store targets, and what goes on to the store.
public sealed class S1
{
    public static Guard Held, Held2;
    public static Guard[] Arr = new Guard[1];
    public static (Guard, int) Tup;
    public static HolderC? HC;
    public static Guard? Maybe;
    public Guard F;
    private static bool s_b, s_fail = true;
    private static int s_i = 5;
    static void Work() { if (s_fail) throw new InvalidOperationException(); }

    // filters
    public static void F01(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch when ((Held = g) is var _ && s_b) { } } catch { g.Dispose(); throw; } }   // F01 store in a filter, then &&
    public static void F02(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch when ((Held = g) is var _ && s_i == 0) { } } catch { g.Dispose(); throw; } } // F02
    public static void F03(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch when (s_b || (Held = g) is var _ && s_b) { } } catch { g.Dispose(); throw; } } // F03
    public static void F04(Lock l) { Guard g = l.Enter(); try { try { Work(); } catch when ((Held = g) is var _ ? s_b : s_b) { } } catch { g.Dispose(); throw; } } // F04
    public static void F05(Lock l) { Guard g = l.Enter(); try { Work(); } catch when ((Held = g) is var _ && s_b) { } catch { g.Dispose(); throw; } }   // F05 later clause of the same try

    // chained and nested assignments
    public static void N01(Lock l) { Guard g = l.Enter(); Held = Held2 = g; }                                                                          // N01 chained assignment
    public static void N02(Lock l) { Guard g = l.Enter(); try { Work(); Held = Held2 = g; } catch { g.Dispose(); throw; } }                         // N02 chained in a try
    public static Guard N03(Lock l) { Guard g = l.Enter(); return Held = g; }                                                                         // N03 return of an assignment
    public static void N04(Lock l) { Guard g = l.Enter(); Sink.Consume(Held = g); }                                                                  // N04 pass an assignment
    public static void N05(Lock l) { Guard g = l.Enter(); var t = (Held = g, 1); }                                                                   // N05 assignment in a tuple
    public static void N06(Lock l) { Guard g = l.Enter(); _ = (Held = g); }                                                                          // N06 discard of an assignment
    public static void N07(Lock l) { Guard g = l.Enter(); var x = Held = g; }                                                                         // N07

    // targets
    public static void T01(Lock l, int i) { Guard g = l.Enter(); try { Work(); Arr[i] = g; } catch { g.Dispose(); throw; } }                          // T01 element, out of range
    public static void T02(Lock l) { Guard g = l.Enter(); try { Work(); Tup.Item1 = g; } catch { g.Dispose(); throw; } }                            // T02 tuple element field
    public static void T03(Lock l) { Guard g = l.Enter(); try { Work(); HC!.G = g; } catch { g.Dispose(); throw; } }                                // T03 field of a null object
    public static void T04(Lock l) { Guard g = l.Enter(); try { Work(); ref Guard r = ref Unsafe.NullRef<Guard>(); r = g; } catch { g.Dispose(); throw; } } // T04 through a null ref
    public static void T05(Lock l, out Guard o) { Guard g = l.Enter(); try { Work(); o = g; } catch { g.Dispose(); throw; } }                        // T05 out
    public static void T06(Lock l, ref Guard o) { Guard g = l.Enter(); try { Work(); o = g; } catch { g.Dispose(); throw; } }                        // T06 ref
    public static void T07(Lock l) { Guard g = l.Enter(); try { Work(); Maybe = g; } catch { g.Dispose(); throw; } }                               // T07 into a nullable
    public static void T08(Lock l) { Guard g = l.Enter(); HolderS h = default; try { Work(); h.G = g; } catch { g.Dispose(); throw; } h.G.Dispose(); } // T08 field of a struct local
    public static void T09(Lock l) { Guard g = l.Enter(); try { Work(); HC = new HolderC { G = g }; } catch { g.Dispose(); throw; } }              // T09 object initializer
    public static void T10(Lock l, HolderR r) { Guard g = l.Enter(); try { Work(); var r2 = r with { G = g }; } catch { g.Dispose(); throw; } }      // T10 with
    public static void T11(Lock l) { Guard g = l.Enter(); try { Work(); (Held, s_i) = (g, 1); } catch { g.Dispose(); throw; } }                     // T11 deconstruction (FP fine)
    public static void T12(Lock l) { Guard g = l.Enter(); try { Work(); Held = s_b ? g : default; } catch { g.Dispose(); throw; } }                 // T12 conditional (FP fine)
    public static void T13(Lock l, (Guard, int) t) { try { Work(); Held = t.Item1; } catch { t.Item1.Dispose(); throw; } }                          // T13 part of a tuple param
    public static void T14(Lock l) { Guard g = l.Enter(); HolderS h = default; try { Work(); h = new HolderS { G = g, X = s_i }; } catch { g.Dispose(); throw; } h.G.Dispose(); } // T14 struct initializer
    public static void T15(Lock l) { Guard g = l.Enter(); try { Work(); HC = new HolderC { G = g }; s_i = 1; } catch { g.Dispose(); throw; } }    // T15 static field after: reported
    public static void T16(Lock l) { Guard g = l.Enter(); try { Work(); Held = (Guard)g; } catch { g.Dispose(); throw; } }                          // T16 identity cast
    public static void T17(Lock l) { Guard g = l.Enter(); try { Work(); Held = g!; } catch { g.Dispose(); throw; } }                                // T17 suppression

    public static Lock Run(Action<Lock> m) { var l = new Lock(); Held = default; Held2 = default; try { m(l); } catch { } Held.Dispose(); Held2.Dispose(); return l; }
}

public static class RTS1
{
    public static Lock F01() => S1.Run(S1.F01);
    public static Lock F02() => S1.Run(S1.F02);
    public static Lock F03() => S1.Run(S1.F03);
    public static Lock F04() => S1.Run(S1.F04);
    public static Lock F05() => S1.Run(S1.F05);
    public static Lock N01() => S1.Run(S1.N01);
    public static Lock N02() => S1.Run(S1.N02);
    public static Lock N06() => S1.Run(S1.N06);
    public static Lock N07() => S1.Run(S1.N07);
    public static Lock T01() => S1.Run(l => S1.T01(l, 3));
    public static Lock T04() => S1.Run(S1.T04);
    public static Lock T03() => S1.Run(S1.T03);
}

public static class RTS1b
{
    public static Lock N04() => S1.Run(S1.N04);
    public static Lock N05() { var l = new Lock(); S1.Held = default; Guard g = l.Enter(); var t = (S1.Held = g, 1); t.Item1.Dispose(); S1.Held.Dispose(); return l; }
}
