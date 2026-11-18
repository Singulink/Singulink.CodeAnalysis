using System;
using Singulink.CodeAnalysis;

public static class Discord
{
    static Guard SomeMethod() => throw new Exception();

    static void X(ref Guard a, ref Guard b)
    {
        b = a;                                  // E01 as posted
        a = SomeMethod();
    }

    static void XMoved([MovedFrom] ref Guard a, ref Guard b)
    {
        b = a;                                  // E02 with the attribute
        a = SomeMethod();                       // E03 throws before the reset
    }

    static void XSafe([MovedFrom] ref Guard a, ref Guard b)
    {
        Guard fresh = SomeMethod();             // E04 anything that can throw comes first
        b = a;
        a = fresh;
    }
}
