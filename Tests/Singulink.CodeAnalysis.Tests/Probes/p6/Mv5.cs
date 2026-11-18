using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Slotted<[NonCopyable] T>
{
    private T _slot = default!;
    public void Put(T v) => _slot = NonCopyable.Move(ref v);
    public void SwapWith([MovedFrom] ref T other) { T o = other; other = NonCopyable.Move(ref _slot); _slot = NonCopyable.Move(ref o); }   // DD01
    public static void Into([MovedFrom] ref T dst, Slotted<T> from) { T d = dst; dst = NonCopyable.Move(ref from._slot); from._slot = NonCopyable.Move(ref d); }  // DD02
}

public sealed class Mv5
{
    private Task<Guard> _t = Owner.EnterTaskAsync();
    public async Task Run()
    {
        var s = new Slotted<Task<Guard>>();
        s.Put(_t);
        var x = Owner.EnterTaskAsync();
        s.SwapWith(ref x);
        Guard a = await NonCopyable.Move(ref x);                              // DD03
        var y = Owner.EnterTaskAsync();
        Slotted<Task<Guard>>.Into(ref y, s);
        Guard b = await NonCopyable.Move(ref y);                              // DD04
    }
}
