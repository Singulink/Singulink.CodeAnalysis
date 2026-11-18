using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[return: Borrowed] public delegate T Producer<[NonCopyable] T>();

public sealed class Prod<[NonCopyable] T>
{
    private T _v = default!;
    public Producer<T> Get() => () => NonCopyable.Move(ref _v);             // EE01
    public static Producer<Prod<T>> Factory() => () => new Prod<T>();        // EE02
}

public sealed class UseProd
{
    public async Task Run()
    {
        var p = new Prod<Task<Guard>>().Get();
        Guard a = await p();                                                  // EE03 stored
        var q = new Prod<Guard>().Get();
        Guard b = q();                                                        // EE04 new
        Func<Guard> f = new Prod<Guard>().Get().Invoke;                       // EE05
    }
}
