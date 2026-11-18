using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Ex3<[NonCopyable] T>
{
    public T _v = default!;
    public T Take() => NonCopyable.Move(ref _v);
    public Expression<Func<T>> C() => () => Take();                    // R01
    public Func<T> Compiled() => C().Compile();                        // R02
    public static T Run(Ex3<T> e) => e.C().Compile()();                // R03
}

public sealed class Ex1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();
    public void Run()
    {
        var e = new Ex3<Task<Guard>>();
        e._v = _t;
        Guard g1 = e.C().Compile()().Result;                           // R04
        Guard g2 = e.Compiled()().Result;                              // R05
        Guard g3 = Ex3<Task<Guard>>.Run(e).Result;                     // R06
    }
}
