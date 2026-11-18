using Singulink.CodeAnalysis;

public static class Discord3
{
    static void XAll([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c)
    {
        b = a;                                  // H01
        c = default;                            // H02
        a = default;                            // H03
    }

    static (Guard, Guard) TakeBoth([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
    {
        Guard x = a;                            // H04
        Guard y = b;                            // H05 the same variable again, if a and b are one
        a = default;
        b = default;
        return (NonCopyable.Move(ref x), NonCopyable.Move(ref y));   // H06
    }

    static void Swap([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
    {
        Guard t = a;                            // H07
        a = b;                                  // H08
        b = NonCopyable.Move(ref t);            // H09
    }

    static void Call()
    {
        Guard g = default;
        var both = TakeBoth(ref g, ref g);      // H10 one variable for both parameters
    }
}
