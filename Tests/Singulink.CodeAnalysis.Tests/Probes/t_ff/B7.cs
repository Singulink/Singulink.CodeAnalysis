using System;
using Singulink.CodeAnalysis;

public sealed class Stats { public int Count; }

public static class B7
{
    static Guard s_held;

    static void Keep([MovedFrom] ref Guard slot, Stats stats)
    {
        (s_held, stats.Count) = (slot, 1);          // the second store fails after the first is done
        slot = default;
    }

    public static Lock Run()
    {
        var l = new Lock();
        Guard g = l.Enter();
        try { Keep(ref g, null); } catch (NullReferenceException) { }
        g.Dispose();
        s_held.Dispose();
        return l;
    }
}
