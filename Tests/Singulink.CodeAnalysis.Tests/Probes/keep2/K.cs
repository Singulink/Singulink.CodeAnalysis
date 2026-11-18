using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Guard : IDisposable { private int _s; public void Dispose() => _s = 0; }

public sealed class Keeper
{
    private List<Guard> _kept = new();
    private (List<Guard>, int) _pair;
    private int _n;

    public void K1(List<Guard> l) { (_kept, _n) = (l, 1); }                 // deconstruction into a field
    public void K2(List<Guard> l) { var t = (l, 1); _pair = t; }           // through a tuple local
    public void K3(List<Guard> l) { _pair = (l, 1); }                      // a tuple into a field
    public void K4(List<Guard> l) { List<Guard> x; (x, _n) = (l, 1); _kept = x; }   // alias through a deconstruction
    public void K5(List<Guard> l) { M(out var x, l); _kept = x; }
    private static void M(out List<Guard> o, List<Guard> i) { o = i; }      // out parameter: SCA1032 now
}
