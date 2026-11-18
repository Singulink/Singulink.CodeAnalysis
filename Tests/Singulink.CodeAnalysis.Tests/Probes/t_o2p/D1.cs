using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class DHolder { public readonly Guard G; public DHolder(Guard g) => G = g; }
public class DGen<T> { public static readonly Guard S = default; static DGen() { DGen<int>.S.Dispose(); } }
[NonCopyable] public struct DOuter { public Guard G; public readonly void Peek2() => G.Dispose(); }
public readonly struct DRo(Lock l) { public int P => 0; }
public static class D1
{
    static readonly DHolder s_h = new(default);
    public static void D01(in Guard g) { using (g) { } }                         // D01 using on in
    public static void D02(DHolder h) { using (h.G) { } }                        // D02 using on readonly field
    public static void D03(in DOuter o) { o.G.Dispose(); }                       // D03 field of in
    public static async Task D04(Guard g) { await Task.Yield(); using (g) { } }  // D04 last use in async
    public static void D05(ref readonly Guard g) { g.Dispose(); }                // D05 ref readonly param
    public static void D06(DHolder h) { ref readonly Guard r = ref h.G; r.Dispose(); } // D06
    public static void D07(DHolder h) { h.G.Dispose(); }                         // D07
    public static void D08(Guard[] a) { ReadOnlySpan<Guard> s = a; s[0].Dispose(); } // D08
    public static void D09(in Guard g) { Action a = () => { }; ((Func<int>)(() => 0))(); var x = g.IsHeld; } // D09 ok
    public static void D10(DHolder h) { foreach (ref readonly var x in new ReadOnlySpan<Guard>(in h.G)) x.Dispose(); } // D10
}
