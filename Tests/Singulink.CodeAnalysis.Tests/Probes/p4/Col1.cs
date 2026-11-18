using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Singulink.CodeAnalysis;
class Col1
{
    private List<Guard> _list = new();
    private Dictionary<int, Guard> _dict = new();
    private Stack<Guard> _stack = new();
    private Queue<Guard> _queue = new();
    private Guard[] _arr = new Guard[2];
    void M()
    {
        _dict.TryGetValue(1, out var a);                       // U01 SCA1030
        if (_dict.TryGetValue(1, out Guard b)) { }             // U02 SCA1030
        foreach (var (k, v) in _dict) { }                      // U03 SCA1030
        foreach (var kv in _dict) { }                          // U04 SCA1030
        foreach (var v2 in _dict.Values) { }                   // U05 SCA1030 or 1020
        _stack.TryPeek(out var c);                             // U06 SCA1030
        _stack.TryPop(out var d);                              // U07 OK (new)
        Guard e = _queue.Peek();                               // U08 SCA1030
        Guard f = _dict.GetValueOrDefault(1);                  // U09 SCA1020/1030
        _list[0] = _list[1];                                   // U10 SCA1030
        var seg = new ArraySegment<Guard>(_arr);
        foreach (Guard g in seg) { }                           // U11 SCA1030
        IReadOnlyList<Guard> ro = _list;
        Guard h = ro[0];                                       // U12 SCA1030
        IEnumerable<Guard> en = _arr;
        foreach (var i in en) { }                              // U13 SCA1030
        var im = ImmutableArray.Create(Owner.Enter());         // U14 ?
        Guard j = im[0];                                       // U15 SCA1030/1020
        ref Guard k = ref CollectionsMarshal.GetValueRefOrNullRef(_dict, 1);  // U16 OK
        Guard l = _dict[1];                                    // U17 SCA1030
        _dict[2] = Owner.Enter();                              // U18 OK
        _dict.Remove(1, out var m);                            // U19 OK
        var pq = new PriorityQueue<Guard, int>();              // U20 SCA1020? priority queue
        _list.Insert(0, Owner.Enter());                        // U21 OK
        _list.Reverse();                                       // U22 ?
        _list.Sort((x, y) => 0);                               // U23 SCA1020
        Span<Guard> sp = CollectionsMarshal.AsSpan(_list);     // U24 OK
        Guard n = sp[0];                                       // U25 SCA1001
    }
}
