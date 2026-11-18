using System;
using Singulink.CodeAnalysis;

public sealed partial class Q01 { public void Use() => g.Dispose(); }                                // E42 the member in the other file
public sealed partial class Q02(Guard g) { }                                                          // E43 the parameter list in this file

public static class RTE
{
    public static Lock Q01() { var l = new Lock(); var q = new Q01(l.Enter()); q.Use(); q.F.Dispose(); return l; }
}
