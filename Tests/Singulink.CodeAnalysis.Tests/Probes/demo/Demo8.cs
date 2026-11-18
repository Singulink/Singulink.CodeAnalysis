using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Bag<[NonCopyable] T>
{
    private T[] _items = new T[16];
    private int _count;

    public void Add(T item) => _items[_count++] = NonCopyable.Move(ref item)!;
    public void AddRange(Span<T> items) { foreach (ref T x in items) Add(NonCopyable.Move(ref x)!); }
    public T Take() => NonCopyable.Move(ref _items[--_count])!;
    public ref T this[int i] => ref _items[i];
}

public class BagUse
{
    private Task<Guard> _pending = null!;
    private readonly List<Task<Guard>> _list = new();
    private readonly Stack<Task<Guard>> _stack = new();

    Bag<Task<Guard>> MakeBag()
    {
        var bag = new Bag<Task<Guard>>();
        bag.AddRange(new[] { _pending, _pending });               // B01
        return bag;                                               // B02 is the bag still the method's own?
    }

    List<Task<Guard>> MakeList()
    {
        var list = new List<Task<Guard>>();
        list.Add(_pending);                                       // B03
        return list;                                              // B04
    }

    async Task Use()
    {
        Guard a = await _list[0];                                 // B05 framework list, stored
        Guard b = await _stack.Pop();                             // B06 framework stack, stored
        Guard c = await MakeList()[0];                            // B07
        Guard d = await MakeBag().Take();                         // B08
    }
}
