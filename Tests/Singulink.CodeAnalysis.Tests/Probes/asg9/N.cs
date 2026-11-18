using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public static class N
{
    public static Guard Held, Held2;
    static void Take(Guard g) => g.Dispose();
    static void Look(in Guard g) { }
    static List<Guard> s_list = new();

    public static void N01(Lock l) { Guard g = l.Enter(); Take(Held = g); }                 // N01 argument: last use of g
    public static void N02(Lock l) { Guard g = l.Enter(); Take(Held = g); g.Dispose(); }    // N02 argument: g used again
    public static void N03() { Take(Held = Held2); }                                         // N03 argument from a field
    public static void N04(Lock l) { Guard g = l.Enter(); var t = (Held = g, 1); t.Item1.Dispose(); } // N04 tuple element
    public static void N05(Lock l) { Guard g = l.Enter(); (Held = g).Dispose(); }             // N05 receiver
    public static void N06(Lock l) { Take(Held = l.Enter()); }          // N06 a new value
    public static void N07(Lock l) { Guard g = l.Enter(); s_list.Add(Held = g); }            // N07 listed member
    public static void N08(Lock l) { Guard g = l.Enter(); Guard[] a = [Held = g]; }          // N08 collection expression
    public static void N09(Lock l) { Guard g = l.Enter(); Guard? m = (Held = g); }           // N09 nullable conversion
    public static void N10(Lock l) { Guard g = l.Enter(); object o = (Held = g); }           // N10 box
    public static Guard N11(Lock l) { Guard g = l.Enter(); return Held = g; }                // N11 return
    public static void N12(Lock l) { Guard g = l.Enter(); Look(Held = g); }                  // N12 in
    public static Lock RunN01() { var l = new Lock(); Held = default; N01(l); Held.Dispose(); return l; }
    public static Lock RunN05() { var l = new Lock(); Held = default; N05(l); Held.Dispose(); return l; }
    public static Lock RunN07() { var l = new Lock(); Held = default; s_list.Clear(); N07(l); Held.Dispose(); s_list[0].Dispose(); return l; }
}
public static class RTN { public static Lock N06() { var l = new Lock(); N.Held = default; N.N06(l); N.Held.Dispose(); return l; } public static Lock N01() => N.RunN01(); public static Lock N05() => N.RunN05(); public static Lock N07() => N.RunN07(); }
