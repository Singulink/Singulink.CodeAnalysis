using System;
using Singulink.CodeAnalysis;

public static class M
{
    public static void M01([MovedFrom] ref Guard p) { Sink.Held = p; int n = -1; Index i = ^n; p = default; }   // M01 ^n between read and assignment
    public static void M02([MovedFrom] ref Guard p) { Sink.Held = p; int n = -1; Range r = n..; p = default; }  // M02 range: reported
}
