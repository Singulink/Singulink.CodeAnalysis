using System;
using Singulink.CodeAnalysis;

public static class Tup1
{
    public static (Guard, int) A() { var r = (Owner.Enter(), 1); return r; }                  // U01 unnamed both sides
    public static (Guard g, int n) B() { var r = (g: Owner.Enter(), n: 1); return r; }        // U02 same names
    public static (Guard g, int n) C() { var r = (Owner.Enter(), 1); return r; }              // U03 names differ
    public static (Guard, int) D() { (Guard x, int y) r = (Owner.Enter(), 1); return r; }     // U04 names differ
    public static (Guard g, int n) E() { (Guard, int) r = (Owner.Enter(), 1); return r; }     // U05
    public static void F() { var r = (Owner.Enter(), 1); (Guard g, int n) s = r; }            // U06 copy: should be SCA1001
}
