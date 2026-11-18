using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public delegate void MoveAction<[NonCopyable] T>([MovedFrom] ref T value);

public sealed class Drainable<[NonCopyable] T>
{
    private readonly T[] _items = new T[16];
    private int _n;
    public void Add(T item) => _items[_n++] = NonCopyable.Move(ref item)!;
    public void Drain(MoveAction<T> action) { for (int i = 0; i < _n; i++) action(ref _items[i]); _n = 0; }
}

public class Processor { public virtual void OnItem(ref Task<Guard> t) { } }

public sealed class MovingProcessor : Processor
{
    public override void OnItem([MovedFrom] ref Task<Guard> t) { t = null!; }          // T01 mark added
}

public class TrustUse
{
    private Task<Guard> _t = null!;
    private readonly RwLock _rwLock = new();

    static ref Task<Guard> ResetTask([MovedFrom] ref Task<Guard> v) { v = null!; return ref v; }

    void Go(Drainable<Task<Guard>> tasks, Drainable<Guard> guards)
    {
        tasks.Drain(([MovedFrom] ref Task<Guard> v) =>
        {
            var mine = NonCopyable.Move(ref v);                                        // T02 a slot of generic code
        });
        guards.Drain(([MovedFrom] ref Guard g) => { g.Dispose(); g = default; });      // T03 guards: clean
        guards.Drain(([MovedFrom] ref Guard g) => { using Guard mine = NonCopyable.Move(ref g); });   // T04 clean
    }

    async Task Go2()
    {
        var a = _rwLock.EnterTaskAsync();
        ResetTask(ref a) = _t;
        Guard g = await NonCopyable.Move(ref a);                                       // T05 after a move that returns a reference
    }
}
