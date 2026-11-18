using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class Own5
{
    private static Task<Guard>[] s_shared = [Owner.EnterTaskAsync()];

    public static void Swap([MovedFrom] ref Task<Guard>[] a) { var x = a; a = s_shared; }   // H01

    public static Task<Guard>[] Laundered()
    {
        var arr = new Task<Guard>[1];
        arr[0] = Owner.EnterTaskAsync();
        Swap(ref arr);                                                  // H02
        return arr;                                                     // H03 returns s_shared as new
    }

    public static async Task Use()
    {
        Guard a = await Laundered()[0];                                 // H04
        Guard b = await Laundered()[0];                                 // H05
    }

    public static List<Task<Guard>> Surprise1(bool c)
    {
        var tasks = new List<Task<Guard>>();
        tasks?.Add(Owner.EnterTaskAsync());                             // S01
        return tasks!;                                                  // S02
    }

    public static async Task<List<Task<Guard>>> Surprise2()
    {
        var tasks = new List<Task<Guard>> { Owner.EnterTaskAsync(), Owner.EnterTaskAsync() };
        await Task.WhenAll((IEnumerable<Task>)tasks);                   // S03
        return tasks;                                                   // S04
    }

    public static List<Task<Guard>> Surprise3()
    {
        var tasks = new List<Task<Guard>> { Owner.EnterTaskAsync() };
        if (tasks is { Count: > 0 }) { }                                // S05
        return tasks;                                                   // S06
    }

    public static async Task<List<Guard>> Surprise4()
    {
        Task<List<Guard>> pending = MakeListAsync();
        var list = await pending;                                       // S07
        return list;                                                    // S08
    }

    public static async Task<Task<List<Guard>>> Surprise4b()
    {
        Task<List<Guard>> pending = MakeListAsync();
        await pending;                                                  // S09
        return pending;                                                 // S10
    }

    public static List<Task<Guard>> Surprise5()
    {
        var tasks = new List<Task<Guard>> { Owner.EnterTaskAsync() };
        Console.WriteLine(tasks.Count);                                 // S11
        foreach (var t in tasks) { if (t.IsCompleted) { } }             // S12
        if (tasks.Count == 0) return new();                             // S13
        return tasks;                                                   // S14
    }

    public static List<Task<Guard>> Surprise6()
    {
        var tasks = new List<Task<Guard>> { Owner.EnterTaskAsync() };
        Log(tasks);                                                     // S15 passed to a plain method
        return tasks;                                                   // S16
    }

    private static void Log(List<Task<Guard>> l) { }
    private static async Task<List<Guard>> MakeListAsync() { await Task.Yield(); return new List<Guard>(); }
}
