using System.Runtime.CompilerServices;
using Singulink.CodeAnalysis;

public static class Discord2
{
    static void X(ref Guard a, ref Guard b, ref Guard c)
    {
        b = a;                                  // F01 as posted
        c = default;
        a = default;
    }

    static void XMoved([MovedFrom] ref Guard a, ref Guard b, ref Guard c)
    {
        b = a;                                  // F02 with the attribute
        c = default;                            // F03 throws for a null reference, before the reset
        a = default;
    }

    static void Call()
    {
        Guard a = default, b = default;
        XMoved(ref a, ref b, ref Unsafe.NullRef<Guard>());   // F04
    }
}
