using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

internal class Probe4
{
    private static Task<Guard> EnterTaskAsync() => null!;

    private static object GetObject() => null!;

    private static void Helper(Task task) { }

    private async Task Erasable(IEnumerable<Task> plainTasks, List<Task<Guard>> typed, Task<Guard>[] arr, System.Collections.IEnumerable untyped, object o, List<object> objs)
    {
        Task plain = EnterTaskAsync();                                  // E01 allowed
        Helper(EnterTaskAsync());                                       // E02 allowed
        Guard g1 = await (Task<Guard>)plain;                            // E03
        Guard g2 = await (Task<Guard>)GetObject();                      // E04
        if (o is Task<Guard> t1) { Guard g3 = await t1; }               // E05
        foreach (Task<Guard> t2 in plainTasks) { Guard g4 = await t2; } // E06
        foreach (Task<Guard> t3 in untyped) { Guard g5 = await t3; }    // E07
        foreach (Task<Guard> t4 in objs) { Guard g6 = await t4; }       // E08
        IEnumerable<Task> cov = typed;                                  // E09 narrow rule
        Array a = arr;                                                  // E10 narrow rule
        var wrapped = Task.FromResult(EnterTaskAsync());                // E11
        Guard g7 = await typed.ToArray()[0];                            // E12
        Guard g8 = await (o as Task<Guard>)!;                           // E13
        Guard g9 = await (o switch { Task<Guard> t6 => t6, _ => throw new Exception() });   // E14
        Guard g10 = await Unsafe.As<Task<Guard>>(o);                    // E15
        Guard g11 = await plainTasks.Cast<Task<Guard>>().First();       // E16
        Guard g12 = await (Task<Guard>)(dynamic)o;                      // E17
        dynamic d = plain;                                              // E18 allowed
        Guard g13 = await d;                                            // E19
        Task.WaitAll(arr);                                              // E20 allowed: listed member
        await Task.WhenAny(EnterTaskAsync(), Task.Delay(1));            // E21 allowed
        object asObject = EnterTaskAsync();                             // E22 allowed
        Guard g14 = ((Task<Guard>)GetObject()).Result;                  // E23
        Guard g15 = ((Task<Guard>)GetObject()).GetAwaiter().GetResult();   // E24
    }
}
