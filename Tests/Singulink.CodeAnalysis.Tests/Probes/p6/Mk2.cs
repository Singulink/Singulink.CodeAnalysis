using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class TaskTaker
{
    public static Task<Guard> s_cached = Owner.EnterTaskAsync();
    public virtual Task<Guard> Take([MovedFrom] ref Task<Guard> t) { var m = t; t = null!; return m; }
}

public sealed class CachingTaker : TaskTaker
{
    public override Task<Guard> Take(ref Task<Guard> t)                  // U01 drops [MovedFrom]
    {
        var m = t;
        t = s_cached;                                                    // U02 leaves a shared task
        return m;
    }
}

public sealed class CachingTaker2 : TaskTaker
{
    public override Task<Guard> Take([MovedFrom] ref Task<Guard> t)      // U03 control
    {
        var m = t;
        t = s_cached;                                                    // U04
        return m;
    }
}

public sealed class Mk2
{
    public async Task Run(TaskTaker taker)
    {
        var a = Owner.EnterTaskAsync();
        taker.Take(ref a);
        Guard g1 = await NonCopyable.Move(ref a);                        // V01
        var b = Owner.EnterTaskAsync();
        taker.Take(ref b);
        Guard g2 = await NonCopyable.Move(ref b);                        // V02
    }
}
