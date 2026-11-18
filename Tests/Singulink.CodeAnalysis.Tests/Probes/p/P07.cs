using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

class P07
{
    // iterator parameters are copied into each enumerator
    static IEnumerable<int> Run(Guard g) { g.Dispose(); yield break; }       // V01 hole?
    static async IAsyncEnumerable<int> RunA(Guard g) { g.Dispose(); await Task.Yield(); yield break; } // V02 hole?
    static IEnumerable<int> Run2(Guard g) { g.Touch(); yield return g.State; } // V03 hole (copies per enum)

    void UseIt()
    {
        var seq = Run(Enter());                                  // V04 OK today
        foreach (var x in seq) { }
        foreach (var x in seq) { }                               // second enumeration disposes a second copy
    }

    // derived classes of containers
    class Guards : List<Guard> { }
    class GuardTask : Task<Guard> { public GuardTask() : base(() => default) { } }

    void Derived(Guards guards, GuardTask gt)
    {
        IList il = guards;                                       // W01 report (untyped)
        object o = guards;                                       // W02 report (opaque)
        ((IDisposable)il[0]!).Dispose();                         // W03 disposes a boxed copy
        Guard g = guards[0];                                     // W04 report
        Array a = guards.ToArray();                              // W05 report
        object og = gt;                                          // W06 erasable? (derived)
        Guard g2 = ((Task<Guard>)og).Result;                     // W07 report
        Guard g3 = gt.Result;                                    // W08 report? (gt param stored)
        foreach (Guard x in guards) { }                          // W09 report
        IEnumerable<Guard> e = guards;                           // W10 OK
    }

    // base.MemberwiseClone
    class C1 { public Guard F; public C1 Clone() => (C1)base.MemberwiseClone(); }   // W20 report
    class C2 { public Guard F; public C2 Clone() => (C2)this.MemberwiseClone(); }   // W21 report
    class C3 { public Guard F; public C3 Clone() => (C3)MemberwiseClone(); }        // W22 report

    // 3.8 counter-example shapes compile?
    ref struct RefHolder { public ref Guard R; }
    static void Make(ref Guard g, out Span<Guard> s) => s = new Span<Guard>(ref g);
    Guard _field;
    void LastUse()
    {
        Guard g = Enter();
        scoped RefHolder h = default;
        h.R = ref g;
        _field = g;                                              // X01 (today: report)
        h.R.Dispose();

        Guard g2 = Enter();
        Make(ref g2, out var s);
        _field = g2;                                             // X02 (today: report)
        s[0].Dispose();
    }
    class PC(Guard g)
    {
        public Guard Field = g;                                  // X03 (today: report)
        public void Release() => g.Dispose();
    }
}
