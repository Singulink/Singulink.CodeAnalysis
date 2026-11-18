using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Builder<[NonCopyable] T>
{
    private T[] _items = new T[8];
    private int _n;
    public Builder<T> Add(T item) { _items[_n++] = NonCopyable.Move(ref item); return this; }
    public T Take() => NonCopyable.Move(ref _items[--_n]);
}

public static class G3
{
    public static T Build<[NonCopyable] T>(Action<Builder<T>> configure)
    {
        var b = new Builder<T>();
        configure(b);
        return b.Take();
    }

    public static Builder<T> Create<[NonCopyable] T>(Action<Builder<T>> configure)
    {
        var b = new Builder<T>();
        configure(b);
        return b;
    }
}

public sealed class Gen3
{
    private Task<Guard> _t = Owner.EnterTaskAsync();

    public async Task Run()
    {
        Guard a = await G3.Build<Task<Guard>>(b => b.Add(_t));           // A01
        Guard b = await G3.Build<Task<Guard>>(b => b.Add(_t));           // A02
        var built = G3.Create<Task<Guard>>(b => b.Add(_t).Add(_t));      // A03
        Guard c = await NonCopyable.Move(ref built).Take();              // A04
    }

    public Task<Guard> Strict()
    {
        var b = new Builder<Task<Guard>>();
        b.Add(Owner.EnterTaskAsync());                                   // A05 quiet: new arg, but returns Builder<T> (container of containers)
        return NonCopyable.Move(ref b).Take();                           // A06
    }
}
