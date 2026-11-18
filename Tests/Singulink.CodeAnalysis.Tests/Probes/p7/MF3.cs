using System;
using Singulink.CodeAnalysis;

public static class Holder { public static Guard Slot; public static Guard Taken; }

public sealed class Sneaky
{
    public override string ToString() { Holder.Taken = NonCopyable.Move(ref Holder.Slot); return ""; }   // E01 clean
}

public static class MF3
{
    public static Guard Take([MovedFrom] ref Guard a, object o)   // E02
    {
        Guard m = a;
        string s = "x" + o;
        a = default;
        return m;
    }
}

public static class RTMF3
{
    public static Lock E03()
    {
        var l = new Lock();
        Holder.Slot = l.Enter();
        using Guard mine = MF3.Take(ref Holder.Slot, new Sneaky());   // E03
        Holder.Taken.Dispose();
        return l;
    }
}
