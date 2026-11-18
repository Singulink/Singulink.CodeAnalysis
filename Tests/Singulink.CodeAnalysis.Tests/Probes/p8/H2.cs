using System;
using System.Collections.Generic;
using System.Linq;
using Singulink.CodeAnalysis;

public static class H2
{
    public static Guard Held;
}

public static class RTH2
{
    public static Lock A01() { var l = new Lock(); var a = new { G = l.Enter() }; a.G.Dispose(); a.G.Dispose(); return l; }                     // A01 receiver of anon getter, twice
    public static Lock A02() { var l = new Lock(); var a = new { G = l.Enter(), N = 1 }; var b = a with { N = 2 }; a.G.Dispose(); b.G.Dispose(); return l; } // A02 anon with
    public static Lock A03() { var l = new Lock(); var a = new { G = l.Enter() }; using (a.G) { } using (a.G) { } return l; }                        // A03 using on anon getter
    public static Lock A04() { var l = new Lock(); var a = new { G = l.Enter() }; H2.Held = NonCopyable.Move(ref H2.Held); a.G.Dispose(); var x = new[] { a }; x[0].G.Dispose(); return l; } // A04 anon in array
    public static Lock A05() { var l = new Lock(); var a = new { G = l.Enter() }; IDisposable d = a.G; d.Dispose(); a.G.Dispose(); return l; }       // A05 boxing anon getter
    public static Lock A06() { var l = new Lock(); var a = new { G = l.Enter() }; Sink.Consume(a.G); Sink.Consume(a.G); return l; }                // A06 pass by value
    public static Lock A07() { var l = new Lock(); var t = Tuple.Create(l.Enter()); t.Item1.Dispose(); t.Item1.Dispose(); return l; }             // A07 Tuple<T>
    public static Lock A08() { var l = new Lock(); var lz = new Lazy<Guard>(() => l.Enter()); lz.Value.Dispose(); lz.Value.Dispose(); return l; }  // A08 Lazy<T>
    public static Lock A09() { var l = new Lock(); var d = new Dictionary<int, Guard> { [1] = l.Enter() }; d[1].Dispose(); d[1].Dispose(); return l; } // A09 dict indexer getter receiver
    public static Lock A10() { var l = new Lock(); var list = new List<Guard> { l.Enter() }; list[0].Dispose(); list[0].Dispose(); return l; }       // A10 list indexer receiver
}
