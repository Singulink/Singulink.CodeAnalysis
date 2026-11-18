using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

class P02
{
    object _box = Enter();
    IDisposable _disp = Enter();
    Task<Guard> _task = EnterTaskAsync();

    void Boxes(object box, IDisposable d, ValueType vt, dynamic dyn)
    {
        Guard a = (Guard)box;                                    // B01 report
        Guard? b = box as Guard?;                                // B02 report
        if (box is Guard c) { }                                  // B03 report
        if (box is Guard { IsHeld: true }) { }                   // B04 report
        if (box is Guard) { }                                    // B05 OK
        Guard e = (Guard)vt;                                     // B06 report
        Guard f = dyn;                                           // B07 report
        Guard? g = (Guard?)box;                                  // B08 report
        (Guard, int) h = ((Guard, int))box;                      // B09 report
        var i = box switch { Guard x => 1, _ => 0 };             // B10 report
        switch (box) { case Guard y: break; }                    // B11 report
        if (box is Guard and { IsHeld: true }) { }               // B13 report
        if (box is not Guard { IsHeld: false }) { }              // B14 report
        bool held = box is Guard { State: > 0 };                 // B15 report (field subpattern)
        ref Guard r = ref Unsafe.Unbox<Guard>(box);              // B16 OK
        Guard j = Unsafe.Unbox<Guard>(box);                      // B17 report
        Guard k = (Guard)(IDisposable)d;                         // B18 report
        Guard l = Unsafe.As<object, Guard>(ref box);             // B19 report (unlisted)
        Guard m = (Guard)_box;                                   // B20 report
        var n = (Guard)box is { IsHeld: true };                  // B21 report
        if (box is Guard[] arr) { Guard o = arr[0]; }            // B22 report o
        Guard p = ((StrongBox<Guard>)box).Value;                 // B23 report
        if (box is IEnumerable<Guard> en) foreach (var q in en) { }  // B24 report q
        if (box is var v && v is Guard w) { }                    // B25 report
        Guard? x2 = box is Guard z ? null : null;                // B26 report z
        Guard u = box is Guard ? (Guard)box : default;           // B27 report cast
        if (d is Guard { } dd) { }                               // B28 report
        if (box is Guard?) { }                                   // B29 OK
        if (box is ValueTuple<Guard> { Item1.IsHeld: true }) { } // B30 report
        if (box is KeyValuePair<int, Guard> { Value.IsHeld: true }) { } // B31 report
    }

    void Erasable(Task plain, object o)
    {
        Task t1 = EnterTaskAsync();                              // C01 OK
        object o1 = EnterTaskAsync();                            // C02 OK
        Guard g1 = await2((Task<Guard>)plain);                   // C03 report? stored
        Guard g2 = ((Task<Guard>)plain).Result;                  // C04 report
        var tt = (Task<Guard>)plain;                             // C05
        Guard g3 = tt.Result;                                    // C06 report
        Guard g4 = (o as Task<Guard>)!.Result;                   // C07 report
        if (o is Task<Guard> { Result.IsHeld: true }) { }        // C08 report? copy of result
        if (o is Task<Guard> { Result: var gr }) { }             // C09 report
        Guard g5 = ((Task<Guard>)(object)EnterTaskAsync()).Result; // C10 report
        Task[] arr = new Task<Guard>[1];                         // C11 report (array)
        Array a2 = new Task<Guard>[1];                           // C12 report
        IEnumerable<Task> en = new List<Task<Guard>>();          // C13 report
        _task.Wait(100);                                         // C14 OK
        string s = $"{_task}";                                   // C15 OK?
        lock (_task) { }                                         // C16 OK
        Task.WhenAll(_task, _task);                              // C17 OK
        var tcs = new TaskCompletionSource<Guard>();
        Task t2 = tcs.Task;                                      // C18 OK
        IAsyncResult ar = EnterTaskAsync();                      // C19 OK
        Guard g6 = ((Task<Guard>)ar).Result;                     // C20 report
        Guard g7 = (ar as Task<Guard>)!.GetAwaiter().GetResult();// C21 report
        Guard g8 = Unsafe.As<Task<Guard>>(o).Result;             // C22 report (unlisted As)
        var ct = (Task<Guard>)plain;
        Guard g9 = ct.ConfigureAwait(false).GetAwaiter().GetResult(); // C23 report
        object o9 = (object)new List<Task<Guard>>();             // C24 report
        Task<Task<Guard>> nested = Task.FromResult(EnterTaskAsync());
        Task tn = nested;                                        // C25 OK (erasable)
        Task<Guard[]> ta = Task.FromResult(new Guard[1]);
        Task tb = ta;                                            // C26 OK?
        Task tc = Task.FromResult(_task);                        // C27 report (container bound to unmarked? FromResult listed)
        Guard g10 = ((Task<Guard>)tc).Result;                    // C28 report
    }

    static Guard await2(Task<Guard> t) => t.Result;              // C29 report (param stored)

    async Task AwaitPlain(Task plain)
    {
        Guard g = await (Task<Guard>)plain;                      // C30 report
        if (plain is Task<Guard> tg) { Guard g2 = await tg; }    // C31 report
        switch (plain) { case Task<Guard> tg2: Guard g3 = await tg2; break; } // C32 report
        var x = plain switch { Task<Guard> tg3 => tg3.Result, _ => default }; // C33 report
    }
}
