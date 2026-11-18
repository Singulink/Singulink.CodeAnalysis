using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class Tup2
{
    public static (Guard g, int n) A() { var r = (Owner.Enter(), 1); return NonCopyable.Move(ref r); }   // U10
    public static (Guard g, string? s) B() { (Guard, string) r = (Owner.Enter(), ""); return r; }        // U11
    public static async ValueTask<(Guard g, int n)> C() { var r = (Owner.Enter(), 1); await Task.Yield(); return r; }  // U12
    public static (Guard g, int n) D(bool c) { var r = (Owner.Enter(), 1); return c ? r : default; }    // U13
}
