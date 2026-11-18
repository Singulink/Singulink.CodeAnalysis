using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public delegate void PlainRef<T>(ref T value);
public delegate void TaskRef(ref Task<Guard> value);

public class Processor
{
    public virtual void OnItem(ref Task<Guard> t) { }
    public void Run(ref Task<Guard> field) => OnItem(ref field);
}

public sealed class MovingProcessor : Processor
{
    public Guard Got;
    public override void OnItem([MovedFrom] ref Task<Guard> t)         // S01 adds [MovedFrom]
    {
        var mine = NonCopyable.Move(ref t);
        Got = NonCopyable.Move(ref mine).Result;                         // S02
    }
}

public interface IVisitor { void Visit(ref Task<Guard> t); }

public sealed class MovingVisitor : IVisitor
{
    public Guard Got;
    public void Visit([MovedFrom] ref Task<Guard> t)                    // S03
    {
        var mine = NonCopyable.Move(ref t);
        Got = NonCopyable.Move(ref mine).Result;
    }
}

public class GenBase<[NonCopyable] T>
{
    protected T _slot = default!;
    protected virtual void OnTake([MovedFrom] ref T v) { }
    public void Flush() => OnTake(ref _slot);                           // S04
}

public sealed class TaskGen : GenBase<Task<Guard>>
{
    public Guard Got;
    public void Put(Task<Guard> t) => _slot = t;                        // S05
    protected override void OnTake([MovedFrom] ref Task<Guard> v)
    {
        var mine = NonCopyable.Move(ref v);
        Got = NonCopyable.Move(ref mine).Result;                         // S06
    }
}

public sealed class Mk1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();
    public Guard A, B;

    public void Run(Processor p, IVisitor v)
    {
        p.OnItem(ref _t);                                                // T01
        v.Visit(ref _t);                                                 // T02
        TaskRef d = ([MovedFrom] ref Task<Guard> x) => { A = NonCopyable.Move(ref x).Result; x = null!; };   // T03
        d(ref _t);                                                       // T04
        PlainRef<Task<Guard>> d2 = Grab;                                 // T05
        d2(ref _t);
    }

    private void Grab([MovedFrom] ref Task<Guard> x) { var m = NonCopyable.Move(ref x); B = NonCopyable.Move(ref m).Result; }
}
