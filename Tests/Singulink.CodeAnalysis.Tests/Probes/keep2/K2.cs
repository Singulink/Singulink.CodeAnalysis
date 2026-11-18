using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Keeper2
{
    private List<Guard> _kept = new();
    private object _o = new();
    private Func<int, Guard> _get = _ => default;

    public void D1(List<Guard> l, dynamic d) { d.Keep(l); }                                  // dynamic call
    public void D2(List<Guard> l) { _get = l.Find is null ? _get : _get; }                   // method group compared
    public void D3(List<Guard> l) { Func<Predicate<Guard>, Guard?> f = null!; }              
    public void D4(List<Guard> l, bool b) { _kept = b switch { true => l, false => _kept }; }
    public void D5(List<Guard> l) { if ((l, 1) is (var x, _)) _kept = x; }
    public void D6(List<Guard> l) { var t = (l, 1); var (x, n) = t; _kept = x; }
    public (List<Guard>, int) D7(List<Guard> l) => (l, 1);                                   // returned in a tuple
    public async Task D8(List<Guard> l) { await Task.Run(() => l.Clear()); }                 // captured
    public void D9(List<Guard> l) { Task.Run(l.Clear); }                                     // method group passed on
    public Action D10(List<Guard> l) { Action a = l.Clear; return a; }                       // delegate returned through a local
    public void D11(List<Guard> l) { foreach (var x in new[] { l }) _kept = x; }
    public void D12(List<Guard> l) { object o = l; _o = o; }
    public void D13(List<Guard> l) { _kept = l is { Count: > 0 } x ? x : _kept; }
    public IEnumerable<List<Guard>> D14(List<Guard> l) { yield return l; }
    public void D15(List<Guard> l) { ref readonly List<Guard> r = ref l; _kept = r; }
    public void D16(List<Guard> l) { List<Guard> x = null!; x ??= l; _kept = x; }
    public void D17(List<Guard> l) { var h = new H { L = l }; }
    public void D18(List<Guard> l) { var a = new { l }; _o = a; }
    public void D19(List<Guard> l) { (List<Guard> a, List<Guard> b) = (l, l); _kept = b; }
}
public sealed class H { public List<Guard> L = new(); }
