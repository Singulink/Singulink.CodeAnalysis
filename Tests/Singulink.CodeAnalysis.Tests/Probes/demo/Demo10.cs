using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class Stash<[NonCopyable] T>
{
    private static T s_slot = default!;

    public static void Put(T value) => s_slot = NonCopyable.Move(ref value)!;

    public static T Swap([MovedFrom] ref T value)                       // S01
    { T old = NonCopyable.Move(ref s_slot)!; s_slot = NonCopyable.Move(ref value)!; return old; }

    [return: Borrowed]
    public static T SwapMarked([MovedFrom] ref T value)                 // S02
    { T old = NonCopyable.Move(ref s_slot)!; s_slot = NonCopyable.Move(ref value)!; return old; }

    public static T TakeAndRefill([MovedFrom] ref T value)              // S03
    { T taken = value; value = NonCopyable.Move(ref s_slot)!; return taken; }
}

public sealed class Builder<[NonCopyable] T>
{
    private T[] _items = new T[4]; private int _n;
    public Builder<T> Add(T item) { _items[_n++] = NonCopyable.Move(ref item)!; return this; }   // S04
    public T Take() => NonCopyable.Move(ref _items[--_n])!;
}

public class OutputSide
{
    private Task<Guard> _pending = null!;
    private Guard _held;
    private readonly RwLock _rwLock = new();

    static T Build<[NonCopyable] T>(Action<Builder<T>> configure)
    { var b = new Builder<T>(); configure(b); return b.Take(); }

    static T Echo<[NonCopyable] T>(T value) => value;

    static T MyMove<[NonCopyable] T>([MovedFrom] ref T value)
    { T moved = value; value = default!; return moved; }

    async Task Use()
    {
        Guard a = await Build<Task<Guard>>(b => b.Add(_pending));       // S05
        Guard e = await Echo(_rwLock.EnterTaskAsync());                 // S06
        Task<Guard> mine = _rwLock.EnterTaskAsync();
        Guard m = await MyMove(ref mine);                               // S07
        Task<Guard> mine2 = _rwLock.EnterTaskAsync();
        Guard s = await Stash<Task<Guard>>.SwapMarked(ref mine2);       // S08
        Guard g = Stash<Guard>.SwapMarked(ref _held);                   // S09
        Guard built = Build<Guard>(b => b.Add(_rwLock.Enter()));        // S10
    }
}
