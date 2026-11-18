using System;
using System.Threading.Tasks;
using TCS = System.Threading.Tasks.TaskCompletionSource<Guard>;

public static class RT4
{
    public static Lock D01()
    {
        var l = new Lock();
        var s = new TCS();
        Ext2.Complete(s, l);
        dynamic d = s;                                    // D01 allowed: TaskCompletionSource<T> is erasable
        d.Task.Result.Dispose();                          // D02 nothing: dynamic
        Guard g = s.Task.Result;                          // D03 the owned read
        g.Dispose();
        return l;
    }

    public static Lock D04()
    {
        var l = new Lock();
        var s = new TCS();
        Ext2.Complete(s, l);
        Action a = () => { dynamic d = s; d.Task.Result.Dispose(); };   // D04
        a();
        Guard g = s.Task.Result;                          // D05
        g.Dispose();
        return l;
    }
}
