using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

class Holder3
{
    public Guard[] _arr = new Guard[4];
    public Task<Guard>[] _tasks = new Task<Guard>[4];
    public Task<Guard> _t = null!;
    public List<Guard> _l = new();

    public Guard[] Arr() => _arr;                                         // C01 not checked (array)
    public ArraySegment<Guard> Seg() => _arr;                              // C02 not checked (view)
    public ArraySegment<Guard> Seg2() => new ArraySegment<Guard>(_arr);    // C03
    public Task<Guard>[] Tasks() => _tasks;                                // C04
    public List<Task<Guard>> Wrap() => new List<Task<Guard>> { _t };       // C05 new list holding a stored task
    public List<Task<Guard>> Wrap2() { var l = new List<Task<Guard>>(); l.Add(_t); return l; }  // C06
    public Task<Guard> Assign() => _t = Owner.EnterTaskAsync();            // C07
    public Task<Guard> Coalesce() => _t ??= Owner.EnterTaskAsync();        // C08
    public async Task<List<Guard>> GetAsync() { await Task.Yield(); return _l; }   // C09
    public IEnumerable<Task<Guard>> Yield() { yield return _t; }           // C10
    public Func<List<Guard>> Lambda() => () => _l;                         // C11
    public List<Guard> LocalFn() { return Get(); List<Guard> Get() => _l; }   // C12
    public static implicit operator List<Guard>(Holder3 h) => h._l;       // C13
    public (List<Guard>, int) Tuple() => (_l, 1);                          // C14
    public List<Guard> Captured() { var l = new List<Guard>(); Action a = () => l.Clear(); _act = a; return l; }  // C15
    private Action? _act;
    public List<Guard> Pattern(object o) { if (o is List<Guard> l) return l; return new(); } // C16
    public List<Guard> Self() => this;                                     // C17 user-defined conversion
    public List<Guard> FromLocalField() { var l = _l; return l; }          // C18
    public List<Guard> TwoStep() { List<Guard> a = new(); List<Guard> b = a; _l = b; return a; }  // C19
    public List<Guard> RefLocal() { ref List<Guard> r = ref _l; return r; } // C20
    public List<Guard> Switch(int i) => i switch { 0 => new(), _ => _l };  // C21
    public List<Guard> OutParam() { Get(out var l); return l; }           // C22
    private void Get(out List<Guard> l) => l = _l;
    public List<Guard> Await2() => Task.Run(() => _l).Result;             // C23
    public List<Guard> WithMove() { var l = _l; _l = null!; return l; }    // C24 a real move written by hand
}

class P3
{
    void M(Holder3 h)
    {
        foreach (Guard g in (IEnumerable<Guard>)h.Arr()) { }          // D01 copy of each element
        Guard a = ((IReadOnlyList<Guard>)h.Arr())[0];                  // D02
        Guard b = new ArraySegment<Guard>(h.Arr())[0];                 // D03
        Guard c = h.Seg()[0];                                          // D04
        foreach (Guard d in h.Seg()) { }                               // D05
        Guard e = h.Seg2()[0];                                         // D06
        Guard f = ((IList<Guard>)h.Seg())[0];                          // D07
        Guard g2 = h._arr[0];                                          // D08 (control) reported
        List<Guard> lst = new List<Guard>(h.Arr());                    // D09
    }

    async Task N(Holder3 h)
    {
        Guard a = await h.Wrap()[0];                                   // D10
        Guard b = await h.Wrap2()[0];                                  // D11
        Guard c = await h.Assign();                                    // D12
        Guard d = await h.Coalesce();                                  // D13
        Guard e = await new ArraySegment<Task<Guard>>(h.Tasks())[0];   // D14
        Guard f = (await h.GetAsync())[0];                             // D15
        Guard g = await NonCopyable.Move(ref h._t);                    // D16 second guard from the task of D12
    }
}
