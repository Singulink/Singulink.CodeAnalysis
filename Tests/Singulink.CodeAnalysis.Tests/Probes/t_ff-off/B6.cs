using System;
using Singulink.CodeAnalysis;

namespace Own
{
    public static class Interlocked
    {
        public static void Exchange(ref Guard location, int value) => throw new InvalidOperationException();
    }
}

public static class B6
{
    static void Replace([MovedFrom] ref Guard slot, ref Guard old)
    {
        old = slot;
        System.Threading.Interlocked.Exchange(ref slot, default);   // throws NotSupportedException for a struct
    }

    static void ReplaceOwn([MovedFrom] ref Guard slot, ref Guard old)
    {
        old = slot;
        Own.Interlocked.Exchange(ref slot, 0);                      // any method of this name in a type of this name
        slot = default;
    }

    public static Lock Run()
    {
        var l = new Lock();
        Guard g = l.Enter(), old = default;
        try { Replace(ref g, ref old); } catch (NotSupportedException) { }
        g.Dispose();
        old.Dispose();
        return l;
    }

    public static Lock RunOwn()
    {
        var l = new Lock();
        Guard g = l.Enter(), old = default;
        try { ReplaceOwn(ref g, ref old); } catch (InvalidOperationException) { }
        g.Dispose();
        old.Dispose();
        return l;
    }
}
