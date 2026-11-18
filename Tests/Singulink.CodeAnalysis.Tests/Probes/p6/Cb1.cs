using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public delegate void MoveAction<[NonCopyable] T>([MovedFrom] ref T value);
public interface IConsumer<[NonCopyable] T> { void Consume([MovedFrom] ref T value); }

public sealed class Drainable<[NonCopyable] T>
{
    private T[] _items = new T[16];
    private int _n;
    public void Add(T item) => _items[_n++] = NonCopyable.Move(ref item);
    public void Drain(MoveAction<T> action) { for (int i = 0; i < _n; i++) action(ref _items[i]); _n = 0; }     // L01
    public void DrainTo(IConsumer<T> consumer) { for (int i = 0; i < _n; i++) consumer.Consume(ref _items[i]); _n = 0; }  // L02
}

public sealed class TaskConsumer : IConsumer<Task<Guard>>
{
    public Guard Last;
    public void Consume([MovedFrom] ref Task<Guard> value)
    {
        Task<Guard> mine = NonCopyable.Move(ref value);                     // L03
        Last = mine.Result;                                                 // L04 reads from a local: stored
    }
}

public sealed class TaskConsumer2 : IConsumer<Task<Guard>>
{
    public Task<Guard>? Kept;
    public void Consume([MovedFrom] ref Task<Guard> value) => Kept = NonCopyable.Move(ref value);   // L05
}

public sealed class Cb1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();
    private Guard _a, _b;

    public async Task Run()
    {
        var d = new Drainable<Task<Guard>>();
        d.Add(_t);                                                          // M01
        d.Add(_t);                                                          // M02
        Guard g1 = default;
        d.Drain((ref Task<Guard> v) => { });                                // M03 lambda without [MovedFrom]
        var d2 = new Drainable<Task<Guard>>();
        d2.Add(_t); d2.Add(_t);
        d2.Drain(([MovedFrom] ref Task<Guard> v) => { _a = NonCopyable.Move(ref v).Result; v = null!; });   // M04
        var d3 = new Drainable<Task<Guard>>();
        d3.Add(_t); d3.Add(_t);
        d3.Drain(Handle);                                                   // M05
        var c = new TaskConsumer2();
        var d4 = new Drainable<Task<Guard>>();
        d4.Add(_t);
        d4.DrainTo(c);                                                      // M06
    }

    private void Handle([MovedFrom] ref Task<Guard> v)
    {
        var mine = NonCopyable.Move(ref v);                                 // M07
        _b = NonCopyable.Move(ref mine).Result;                             // M08 a read from a new task
    }

    private async Task HandleAsync(Task<Guard> t) { await Task.Yield(); }
}
