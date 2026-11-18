using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class TaskQueue : Queue<Task<Guard>>
{
    public static Task<Guard> s_t = Owner.EnterTaskAsync();
    public TaskQueue() { Enqueue(s_t); }
}

public sealed class MyQ<[NonCopyable] T> : Queue<T> { }                         // CC01

public sealed class MyList<[NonCopyable] T> : IReadOnlyList<T>                  // CC02
{
    private T[] _a = new T[4];
    public T this[int index] => NonCopyable.Move(ref _a[index]);                 // CC03
    public int Count => 4;
    public IEnumerator<T> GetEnumerator() { for (int i = 0; i < 4; i++) yield return NonCopyable.Move(ref _a[i]); }   // CC04
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();                  // CC05
    public void Set(int i, T v) => _a[i] = NonCopyable.Move(ref v);
}

public sealed class Seam1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();
    public async Task Run()
    {
        var q = new TaskQueue();
        Guard a = await q.Dequeue();                                            // CC10
        Guard b = await NonCopyable.Move(ref q).Dequeue();                      // CC11
        Guard c = await new TaskQueue().Dequeue();                              // CC12
        Guard d = await new MyQ<Task<Guard>>().Dequeue();                       // CC13
        var ml = new MyList<Task<Guard>>();
        ml.Set(0, _t);
        Guard e = await new MyList<Task<Guard>>()[0];                           // CC14
        IReadOnlyList<Task<Guard>> view = new MyList<Task<Guard>>();            // CC15
        Guard f = await view[0];                                                // CC16
        foreach (var t in new MyList<Task<Guard>>()) { Guard g = await t; }     // CC17
        Guard h = await ((IReadOnlyList<Task<Guard>>)new MyList<Task<Guard>>())[0];   // CC18
        Guard i = await ToList(new MyList<Task<Guard>>())[0];                   // CC19
        Guard j = await Wrap(new MyList<Task<Guard>>())[0];                     // CC20
    }
    static List<Task<Guard>> ToList(IEnumerable<Task<Guard>> e) { var l = new List<Task<Guard>>(); foreach (var x in e) l.Add(x); return l; }  // CC21
    static IReadOnlyList<Task<Guard>> Wrap(IReadOnlyList<Task<Guard>> e) => e;   // CC22
}
