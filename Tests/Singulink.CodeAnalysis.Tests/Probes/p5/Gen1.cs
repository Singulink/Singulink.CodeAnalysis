using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public delegate bool OutFactory<[NonCopyable] T>(out T value);
public delegate void RefAction<[NonCopyable] T>(ref T value);
public interface IFiller<[NonCopyable] T> { void Fill(out T value); }

public sealed class Bag<[NonCopyable] T>
{
    private T[] _items = new T[16];
    private int _n;
    public void Add(T item) => _items[_n++] = NonCopyable.Move(ref item);
    public T Take() => NonCopyable.Move(ref _items[--_n]);
    public ref T this[int i] => ref _items[i];
    public void AddFrom(OutFactory<T> f) { if (f(out T v)) _items[_n++] = NonCopyable.Move(ref v); }
    public void Update(RefAction<T> f) { for (int i = 0; i < _n; i++) f(ref _items[i]); }
    public void FillSpan(Action<Span<T>> f) { f(_items.AsSpan(_n, 2)); _n += 2; }
    public void AddFrom(IFiller<T> f) { f.Fill(out T v); _items[_n++] = NonCopyable.Move(ref v); }
    public Action<T> Adder() => Add;
    public Writer GetWriter() => new Writer(this);
    public sealed class Writer { private readonly Bag<T> _b; public Writer(Bag<T> b) => _b = b; public void Put(T v) => _b.Add(NonCopyable.Move(ref v)); }
}

public static class Slot<[NonCopyable] T> { public static T Value = default!; }

public sealed class Hub
{
    public void Put<[NonCopyable] T>(T v) => Slot<T>.Value = NonCopyable.Move(ref v);
    public T Take<[NonCopyable] T>() => NonCopyable.Move(ref Slot<T>.Value);
    public static T TakeStatic<[NonCopyable] T>() => NonCopyable.Move(ref Slot<T>.Value);
}

public static class G
{
    public static T Make<[NonCopyable] T>(OutFactory<T> f) { f(out T v); return NonCopyable.Move(ref v); }
    public static T Make2<[NonCopyable] T>(IFiller<T> f) { f.Fill(out T v); return NonCopyable.Move(ref v); }
    public static T Make3<[NonCopyable] T>(Func<T> f) => f();
}

public sealed class CachedFiller : IFiller<Task<Guard>>
{
    private readonly Task<Guard> _t;
    public CachedFiller(Task<Guard> t) => _t = t;
    public void Fill(out Task<Guard> value) => value = _t;                // A01 should this be reported?
}

public sealed class StaticFiller : IFiller<Task<Guard>>
{
    public static Task<Guard> Shared = Owner.EnterTaskAsync();
    public void Fill(out Task<Guard> value) => value = Shared;            // A02
}

public sealed class Gen1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();
    private readonly Hub _hub = new();

    public async Task Run()
    {
        // H1: instance generic method on a receiver that carries nothing, over static state
        _hub.Put(_t);                                                   // B01
        Guard a = await _hub.Take<Task<Guard>>();                       // B02 hole if clean
        _hub.Put(_t);                                                   // B03
        Guard b = await _hub.Take<Task<Guard>>();                       // B04 hole if clean
        Guard c = await Hub.TakeStatic<Task<Guard>>();                  // B05 expected SCA1030
        Guard d = await new Hub().Take<Task<Guard>>();                  // B06

        // H2: callbacks hand stored containers in through out/ref/span
        Guard e = await G.Make<Task<Guard>>((out Task<Guard> v) => { v = _t; return true; });   // C01
        Guard f = await G.Make<Task<Guard>>((out Task<Guard> v) => { v = _t; return true; });   // C02
        Guard g = await G.Make2(new StaticFiller());                    // C03
        Guard h = await G.Make2(new StaticFiller());                    // C04
        Guard i = await G.Make3(() => _t);                              // C05 lambda returns a field: SCA1031 expected
    }

    public Task<Guard> Laundered1()
    {
        var bag = new Bag<Task<Guard>>();
        bag.AddFrom((out Task<Guard> v) => { v = _t; return true; });  // D01
        return NonCopyable.Move(ref bag).Take();                        // D02 hole if clean
    }

    public Task<Guard> Laundered2()
    {
        var bag = new Bag<Task<Guard>>();
        bag.AddFrom(new StaticFiller());                                // D03
        return NonCopyable.Move(ref bag).Take();                        // D04
    }

    public Task<Guard> Laundered3()
    {
        var bag = new Bag<Task<Guard>>();
        bag.Add(Owner.EnterTaskAsync());
        bag.Update((ref Task<Guard> slot) => slot = _t);                // D05
        return NonCopyable.Move(ref bag).Take();                        // D06
    }

    public Task<Guard> Laundered4()
    {
        var bag = new Bag<Task<Guard>>();
        bag.FillSpan(s => { s[0] = _t; s[1] = _t; });                   // D07
        return NonCopyable.Move(ref bag).Take();                        // D08
    }

    public Task<Guard> Laundered5()
    {
        var bag = new Bag<Task<Guard>>();
        var add = bag.Adder();                                          // D09 result is a delegate
        add(_t);                                                        // D10
        return NonCopyable.Move(ref bag).Take();                        // D11
    }

    public Task<Guard> Laundered6()
    {
        var bag = new Bag<Task<Guard>>();
        var w = bag.GetWriter();                                        // D12 result is a nested class
        w.Put(_t);                                                      // D13
        return NonCopyable.Move(ref bag).Take();                        // D14
    }

    public Bag<Task<Guard>> Laundered7()
    {
        var bag = new Bag<Task<Guard>>();
        bag.Add(Owner.EnterTaskAsync());
        bag.Update((ref Task<Guard> slot) => slot = _t);                // D15
        return bag;                                                     // D16
    }
}
