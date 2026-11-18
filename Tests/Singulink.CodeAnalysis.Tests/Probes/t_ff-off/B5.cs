using System;
using System.Runtime.CompilerServices;
using Singulink.CodeAnalysis;

public sealed class Probe : ITuple
{
    public object this[int index] => null;
    public int Length => throw new InvalidOperationException();
}

public sealed class Owner
{
    public Guard Held;
    public bool IsEmpty;
    public object State = new Probe();

    public void Take(Guard g)
    {
        try { Held = g; IsEmpty = State is (); }    // 'is ()' calls ITuple.Length
        catch { g.Dispose(); throw; }
    }
}

public static class B5
{
    static void Replace([MovedFrom] ref Guard slot, ref Guard old, object state)
    {
        old = slot;
        bool isEmpty = state is ();
        slot = default;
    }

    public static Lock RunCommit()
    {
        var l = new Lock();
        var owner = new Owner();
        try { owner.Take(l.Enter()); } catch (InvalidOperationException) { }
        owner.Held.Dispose();
        return l;
    }

    public static Lock RunMovedFrom()
    {
        var l = new Lock();
        Guard g = l.Enter(), old = default;
        try { Replace(ref g, ref old, new Probe()); } catch (InvalidOperationException) { }
        g.Dispose();
        old.Dispose();
        return l;
    }
}
