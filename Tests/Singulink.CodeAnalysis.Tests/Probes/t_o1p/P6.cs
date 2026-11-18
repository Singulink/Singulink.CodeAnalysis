using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Singulink.CodeAnalysis;

public static class Keepers
{
    public static List<Guard>? Kept;
    public static void KeepSpan(params ReadOnlySpan<List<Guard>> ls) => Kept = ls[0];      // K10
    public static void KeepArr(params List<Guard>[] ls) => Kept = ls[0];                  // K11
    public static void KeepTuple((List<Guard> L, int N) p) => Kept = p.L;                 // K12
    public static void KeepIn(in List<Guard> l) => Kept = l;                              // K13
    public static void KeepNullable(List<Guard>? l) => Kept = l;                          // K14
    extension(List<Guard> l)
    {
        public void Register() => Kept = l;                                               // K15
        public List<Guard> Self => l;                                                     // K16
    }
    public static void KeepRef(ref List<Guard> l) => Kept = l;                            // K17
    public static void KeepEnum(IEnumerable<Guard> e) { if (e is List<Guard> l) Kept = l; }   // K18
    public static void KeepSwitch(IEnumerable<Guard> e) { switch (e) { case List<Guard> l: Kept = l; break; } }  // K19
    public static void KeepAs(IEnumerable<Guard> e) => Kept = e as List<Guard>;           // K20
    public static void KeepCast(IReadOnlyList<Guard> e) => Kept = (List<Guard>)e;         // K21
    public static void KeepLocal(List<Guard> e) { var x = e; Action a = () => Kept = x; a(); }  // K22
    public static void KeepViaOut(List<Guard> e, out List<Guard> o) => o = e;             // K23
}

public static class RT6
{
    static List<Guard> M(Lock l, int how)
    {
        var list = new List<Guard>();
        list.Add(l.Enter());
        switch (how)
        {
            case 10: Keepers.KeepSpan(list); break;
            case 11: Keepers.KeepArr(list); break;
            case 15: list.Register(); break;
            case 18: Keepers.KeepEnum(list); break;
            case 20: Keepers.KeepAs(list); break;
            case 21: Keepers.KeepCast(list); break;
        }
        return list;                                                                       // K30
    }

    static Lock Run(int how)
    {
        var l = new Lock();
        foreach (Guard g in M(l, how)) g.Dispose();
        CollectionsMarshal.AsSpan(Keepers.Kept)[0].Dispose();
        return l;
    }
    public static Lock K10() => Run(10);
    public static Lock K11() => Run(11);
    public static Lock K15() => Run(15);
    public static Lock K18() => Run(18);
}
