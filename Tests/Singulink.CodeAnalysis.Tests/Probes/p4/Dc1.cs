using Singulink.CodeAnalysis;

[NonCopyable]
public struct GPair
{
    private Guard _g;
    public int N;
    public void Deconstruct(out Guard g, out int n) { g = NonCopyable.Move(ref _g); n = N; }
}

class Dc1
{
    private readonly GPair _ro;
    private GPair _rw;
    void M(in GPair p, ref readonly GPair q)
    {
        var (g1, n1) = p;          // DC01 runs on a defensive copy: g1 is a second guard
        var (g2, n2) = _ro;        // DC02
        var (g3, n3) = q;          // DC03
        var (g4, n4) = _rw;        // DC04 OK: in place, moves out
        (Guard g5, int n5) = p;    // DC05
    }
}
class Dc2
{
    void M(in GPair p)
    {
        if (p is (var g, _)) { }            // DC06
        foreach (var (g2, n2) in new GPair[1]) { }   // DC07 copy of element anyway
    }
}
class Dc3
{
    private GPair[] _arr = new GPair[1];
    private System.Collections.Generic.List<GPair> _list = new();
    void M(System.Span<GPair> span)
    {
        foreach (var (g, n) in _arr) { }     // DC08 copy of each element
        foreach (var (g, n) in _list) { }    // DC09
        foreach (var (g, n) in span) { }     // DC10
        foreach (var e in _arr) { }          // DC11 contrast
    }
}
