using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public sealed unsafe class Keeper3
{
    private List<Guard> _kept = new();
    private static void Store([Kept] List<Guard> l) { }

    public void E1(List<Guard> l) { var t = ((l, 1), 2); _kept = t.Item1.Item1; }                 // nested tuple local
    public void E2(List<Guard> l) { var t = (l, 1); ref var r = ref t; _kept = r.Item1; }         // ref to a tuple local
    public void E3(List<Guard> l) { delegate*<List<Guard>, void> f = &Store; f(l); }             // function pointer to a method that keeps
    public void E4(List<Guard> l) { Span<List<Guard>> s = [l]; }                                  // collection expression
    public void E5(List<Guard> l) { var t = (a: l, n: 1); _kept = t.a; }                          // named element
    public void E6(List<Guard> l) { var t = (l, 1); var u = t; _kept = u.Item1; }                 // tuple copied to another local
    public void E7(List<Guard> l) { (List<Guard> a, int n) t = default; t.a = l; _kept = t.a; }   // assigned to an element of a tuple local
}
