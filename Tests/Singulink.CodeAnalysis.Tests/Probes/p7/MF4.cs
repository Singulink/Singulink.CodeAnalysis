using System;
using Singulink.CodeAnalysis;

public static class MF4
{
    static void Fail() => throw new Exception();

    // the read is in a filter that fails; the next catch returns without assigning
    public static Guard G01([MovedFrom] ref Guard a, bool c)   // G01 must be reported
    {
        Guard m = default;
        try { Fail(); }
        catch when ((m = a) is { } && c) { a = default; }
        catch { }
        return m;
    }
}

public static class RTMF4
{
    static Guard s_slot;
    public static Lock G02()
    {
        var l = new Lock();
        s_slot = l.Enter();
        using Guard mine = MF4.G01(ref s_slot, false);   // G02
        s_slot.Dispose();
        return l;
    }
}
