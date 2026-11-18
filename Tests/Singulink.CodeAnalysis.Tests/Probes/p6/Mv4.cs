using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class Dup
{
    public static (T, T) Twice<[NonCopyable] T>([MovedFrom] ref T v)      // N01
    {
        var r = (v, v);
        v = default!;
        return r;
    }

    public static (T, T) Twice2<[NonCopyable] T>([MovedFrom] ref T v)     // N02
    {
        T a = v;
        T b = v;
        v = default!;
        return (a, b);
    }

    public static (T, T) Loop<[NonCopyable] T>([MovedFrom] ref T v)       // N03
    {
        T a = default!, b = default!;
        for (int i = 0; i < 2; i++) { if (i == 0) a = v; else b = v; }
        v = default!;
        return (a, b);
    }

    public static (T, T) Both<[NonCopyable] T>([MovedFrom] ref T a, [MovedFrom] ref T b)   // N04
    {
        T x = a; a = default!;
        T y = b; b = default!;
        return (x, y);
    }
}

public sealed class Mv4
{
    public async Task Run()
    {
        var t = Owner.EnterTaskAsync();
        var (p, q) = Dup.Twice(ref t);                         // O01
        Guard g1 = await NonCopyable.Move(ref p);              // O02
        Guard g2 = await NonCopyable.Move(ref q);              // O03
        var t2 = Owner.EnterTaskAsync();
        var pair = Dup.Twice2(ref t2);                         // O04
        Guard g3 = await pair.Item1;                           // O05
        var t3 = Owner.EnterTaskAsync();
        var (r, s) = Dup.Both(ref t3, ref t3);                 // O06
    }
}

public sealed class Add3<[NonCopyable] T>
{
    private T _v = default!;
    public Func<T> A() => () => default!;                        // P01
    public Func<T> B() => () => NonCopyable.Move(ref _v);        // P02
    public Expression<Func<T>> C() => () => default!;            // P03
    public Func<Add3<T>> D() => () => new Add3<T>();             // P04
    public Func<int, T> E() => i => NonCopyable.Move(ref _v);    // P05
    public T Take() => NonCopyable.Move(ref _v);
    public Func<T> F() => Take;                                  // P06
    public Func<T> G() => new Func<T>(Take);                     // P07
    public T H(Func<T> make) => make();                          // P08 calls a given factory
    public static Add3<T> Create(Func<T> make) { var a = new Add3<T>(); a._v = make(); return a; }  // P09
}

public sealed class Add2
{
    public event Func<Task<Guard>>? Ev;
    public void Run(Bag<Task<Guard>> bag, Add3<Task<Guard>> a3)
    {
        Func<Task<Guard>> f1 = bag.Take;                         // Q01
        var f2 = bag.Take;                                       // Q02
        Ev += bag.Take;                                          // Q03
        Use(bag.Take);                                           // Q04
        Func<Task<Guard>> f5 = new(bag.Take);                    // Q05
        Func<Task<Guard>> f6 = a3.Take;                          // Q06
        Func<Task<Guard>> f7 = Owner.EnterTaskAsync;             // Q07 clean
        Func<Task<Guard>> f8 = () => bag.Take();                 // Q08
        Func<Task<Guard>> f9 = new System.Collections.Generic.Queue<Task<Guard>>().Dequeue;   // Q09
        Func<Task<Guard>> f10 = a3.F();                          // Q10 delegate from the generic
        Guard g = a3.F()().Result;                               // Q11
    }
    static void Use(Func<Task<Guard>> f) { }
}
