using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class GuardList : List<Guard>
{
    public void Register(Bag<GuardList> b) => b.Add(this);                   // P01 this into marked T: SCA1033?
    public GuardList Fluent() { Add(Owner.Enter()); return this; }            // P02 SCA1031 expected? (fluent builder)
    public static GuardList operator +(GuardList l, Guard g) { l.Add(g); return l; } // P03 returns a parameter
}

class Own4
{
    private Task<Guard> _t = null!;

    Task<Guard> WithTimeout(Task<Guard> t, CancellationToken ct) => t.WaitAsync(ct);   // P04 SCA1031 expected
    ValueTask<Guard> Wrap(Task<Guard> task) => new(task);                              // P05 SCA1032 (judgment call)
    ValueTask<Guard> Wrap2([Kept] Task<Guard> task) => new(task);                      // P06 OK
    Task<Guard> Relay(Task<Guard> t) => t;                                             // P07 SCA1031

    async Task Callers(CancellationToken ct)
    {
        var t = Owner.EnterTaskAsync();
        var w = t.WaitAsync(ct);
        Guard a = await NonCopyable.Move(ref t);                                       // P08 OK (own local)
        Guard b = await NonCopyable.Move(ref w);                                       // P09 SCA1033 expected
        var t2 = Owner.EnterTaskAsync();
        var vt = Wrap2(t2);
        Guard c = await NonCopyable.Move(ref t2);                                      // P10 SCA1033 expected (kept)
        Guard d = await NonCopyable.Move(ref vt);                                      // P11 OK
        var gl = new GuardList().Fluent();
        Guard e = gl.Fluent()[0];                                                      // P12 ?
        var bag = new Bag<GuardList>();
        var mine = new GuardList();
        mine.Register(bag);                                                            // P13 receiver kept: known gap
        Guard f = bag.Take()[0];                                                       // P14 new by rule
        Guard g = NonCopyable.Move(ref mine)[0];                                       // P15 second "new" path
        var t3 = Owner.EnterTaskAsync();
        var cont = t3.ContinueWith(x => 1);                                            // P16 SCA1020?
        var t4 = Owner.EnterTaskAsync();
        Task plain = t4;                                                               // P17 erasable: allowed. Does it make t4 not own?
        Guard h = await NonCopyable.Move(ref t4);                                      // P18
        Guard i = await (Task<Guard>)plain;                                            // P19 SCA1030
        var t5 = Owner.EnterTaskAsync();
        object boxedT5 = t5;                                                           // P20 erasable via object
        _ = Task.Run(async () => { Guard j = await (Task<Guard>)boxedT5; });          // P21 SCA1030
        Guard k = await NonCopyable.Move(ref t5);                                      // P22
    }

    static Task<Guard> Launder(Task plain) => (Task<Guard>)plain;                      // P23 SCA1031 expected (from a conversion: stored)
    static Task<Guard> Launder2(object o) => o as Task<Guard> ?? throw null!;          // P24 SCA1031 expected
    static Task<Guard> Launder3(object o) => o is Task<Guard> t ? t : null!;           // P25 SCA1031 expected
    static Task<Guard> Launder4(object o) { if (o is Task<Guard> t) return t; return null!; } // P26
    static Task<Guard> Launder5(object o) => o switch { Task<Guard> t => t, _ => null! };     // P27
    static Task<Guard> Launder6(dynamic o) => o;                                       // P28
    static Task<Guard> Launder7(Task plain) => Unsafe2.As(plain);                      // P29
}

static class Unsafe2
{
    public static Task<Guard> As(Task t) => (Task<Guard>)t;                            // P30 SCA1031
}
