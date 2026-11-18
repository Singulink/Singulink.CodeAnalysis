using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// Ownership: try to launder a stored container into a "new" one.
class Own1
{
    private Task<Guard> _t = null!;
    private Task<Guard>? _tn;
    private List<Guard> _l = new();
    private Stack<Task<Guard>> _stack = new();
    private Queue<Task<Guard>> _queue = new();
    private Dictionary<string, Task<Guard>> _dict = new();
    private Task<Guard>[] _arr = new Task<Guard>[4];
    private ConcurrentDictionary<string, Task<Guard>> _cd = new();

    // Listed members called on a stored receiver hand out stored containers; is a return of them reported?
    Task<Guard> R1() => Volatile.Read(ref _t);                               // A01 should SCA1031
    Task<Guard> R2() => Interlocked.CompareExchange(ref _t, null!, null!);   // A02 should SCA1031
    Task<Guard> R3() => _stack.Peek();                                       // A03 should SCA1031
    Task<Guard> R4() => _queue.Peek();                                       // A04 should SCA1031
    Task<Guard> R5() { _dict.TryGetValue("a", out var t); return t!; }       // A05 should SCA1031 (out-declared)
    Task<Guard> R6() { Task<Guard>? t; _dict.TryGetValue("a", out t); return t!; } // A06 should SCA1031
    Task<Guard> R7() { var t = Owner.EnterTaskAsync(); _dict.TryGetValue("a", out t!); return t; } // A07 should SCA1031
    Task<Guard> R8() => _dict.GetValueOrDefault("a")!;                        // A08 should 1020 or 1031
    Task<Guard> R9() => _cd.GetOrAdd("a", _ => Owner.EnterTaskAsync());      // A09 should 1031 (or 1020)
    Task<Guard> R10() => Interlocked.Exchange(ref _t, null!);                 // A10 take-out: OK-ish? (a move out of a field)
    Task<Guard> R11() => Volatile.Read(ref _arr[0]);                          // A11 should SCA1031
    Task<Guard> R12() => Unsafe.As<Task<Guard>>(_t);                          // A12 should SCA1031
    Task<Guard> R13() => CollectionsMarshal.GetValueRefOrNullRef(_dict, "a"); // A13 should SCA1031 (ref read)
    Task<Guard> R14() => _arr.AsSpan()[0];                                    // A14 should SCA1031
    Task<Guard> R15() => MemoryMarshal.GetArrayDataReference(_arr);           // A15 should SCA1031
    Task<Guard> R16() => _tn ?? Owner.EnterTaskAsync();                       // A16 should SCA1031
    Task<Guard> R17() { Task<Guard> t = Owner.EnterTaskAsync(); (t, _) = (_t, 0); return t; } // A17 should SCA1031
    Task<Guard> R18() { var t = Owner.EnterTaskAsync(); t ??= _t; return t; } // A18 should SCA1031 (?? branch)
    Task<Guard> R19() { foreach (var t in _arr) return t; return null!; }    // A19 should SCA1031
    Task<Guard> R20() { var t = Owner.EnterTaskAsync(); ref var r = ref t; r = _t; return t; } // A20 contrived
    Task<Guard> R21() { var t = Owner.EnterTaskAsync(); Assign(ref t, _t); return t; } // A21 should SCA1031
    static void Assign(ref Task<Guard> dst, Task<Guard> src) => dst = src;    // A22 keeps src? (assigned through ref param)
    Task<Guard> R23() => _stack.Pop();                                        // A23 new? stack pop of stored stack: container follows default
    Task<Guard> R24() { _stack.TryPop(out var t); return t!; }               // A24 should SCA1031
    Task<Guard> R25() => Task.FromResult(_t).Result;                           // A25
    Task<Guard> R26() => _t.ContinueWith(x => x.Result).Result.GetHashCode() == 0 ? null! : null!;      // A26 n/a?
    async Task<Task<Guard>> R27() { await Task.Yield(); return _t; }          // A27 should SCA1031
    Task<Guard> R28() => Get(this);                                           // A28 helper on param
    static Task<Guard> Get(Own1 o) => o._t;                                    // A29 should SCA1031
    Task<Guard> R30() { var a = _arr; return a[0]; }                          // A30 should SCA1031
    Task<Guard> R31() { var a = new Task<Guard>[1]; a[0] = _t; return a[0]; } // A31 should SCA1031
    Task<Guard> R32() { var l = new List<Task<Guard>>(); l.Add(_t); return l[0]; } // A32 should SCA1031
    Task<Guard> R33() { var l = new List<Task<Guard>> { _t }; return l[0]; }  // A33 should SCA1031
    Task<Guard> R34() { var l = new List<Task<Guard>>(); l.Add(_t); return l.Last(); } // A34

    async Task Consume()
    {
        Guard g1 = await R1();   // B01 the caller
        Guard g2 = await _stack.Peek(); // B02 should SCA1030
        Guard g3 = await Volatile.Read(ref _t); // B03 should SCA1030
        Guard g4 = await _stack.Pop(); // B04 should SCA1030 (container from stored stack)
        Guard g5 = await Interlocked.Exchange(ref _t, null!); // B05 move out of field?
        var l = new List<Task<Guard>>(); l.Add(_t);
        Guard g6 = await l[0];  // B06 should SCA1030
        Guard g7 = await NonCopyable.Move(ref l)[0]; // B07 l holds a stored task: should report
        Guard g8 = await new List<Task<Guard>> { _t }[0]; // B08 should SCA1030
        Guard g9 = await new[] { _t }[0]; // B09 should SCA1030 (array element ref anyway)
        Guard g10 = await Task.WhenAny(_t).Result; // B10
        Guard g11 = await await Task.WhenAny(_t, _t); // B11 should SCA1030
    }
}

static class OwnExt
{
    public static T Last<T>(this List<T> l) => l[^1];
}
