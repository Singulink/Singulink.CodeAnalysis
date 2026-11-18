using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;
class Fe1
{
    private (Guard Guard, int Id)[] _tuples = new (Guard, int)[2];
    private List<(Guard, int)> _tl = new();
    private KeyValuePair<int, Guard>[] _kvs = new KeyValuePair<int, Guard>[1];
    void M(Span<(Guard, int)> span, ReadOnlySpan<(Guard, int)> ros)
    {
        foreach (var (g, id) in _tuples) { g.Dispose(); }       // FE01 copy of each guard
        foreach ((Guard g, int id) in span) { }                  // FE02
        foreach (var (g, id) in ros) { }                         // FE03
        foreach (var (g, id) in _tl) { }                         // FE04 SCA1030
        foreach (var (k, v) in _kvs) { }                         // FE05
        foreach (var t in _tuples) { }                           // FE06 contrast SCA1001
        var (a, b) = _tuples[0];                                 // FE07 contrast
    }
}
