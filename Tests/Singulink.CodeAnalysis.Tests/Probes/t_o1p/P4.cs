using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public sealed class Bag<[NonCopyable] T> where T : struct
{
    public T[] Items = new T[4];
    public ref T Slot(int i) => ref Items[i];
}

public static class BagExt
{
    extension<[NonCopyable] T>(Bag<T> bag) where T : struct
    {
        public T DupA() => bag.Items[0];                                // G01
        public T DupP => bag.Items[0];                                  // G01b
        public static T DupS(Bag<T> b) => b.Items[0];                   // G01c
    }

    extension<T>(Bag<T> bag) where T : struct
    {
        public T DupU() => bag.Items[0];                                // G01d unmarked
    }
}

public interface ISlot<[NonCopyable] T> where T : struct
{
    ref T Slot { get; }
    T Dup() => Slot;                                                    // G02
    T Dup2() { T x = Slot; return x; }                                  // G02b
    static abstract ref T Shared { get; }
    static virtual T DupStatic() => default;
}

public sealed class Outer<[NonCopyable] T> where T : struct
{
    public T[] Items = new T[4];
    public sealed class Inner
    {
        public T Dup(Outer<T> o) => o.Items[0];                         // G12
    }
    public T ViaLocal()
    {
        return Local(ref Items[0]);
        static TT Local<TT>(ref TT x) => x;                             // G04 unmarked local generic
    }
    public T ViaLocalMarked()
    {
        return Local(ref Items[0]);
        static TT Local<[NonCopyable] TT>(ref TT x) => x;               // G04b
    }
    public Func<T> ViaLambda() => () => Items[0];                       // G05
    public (T, int) Pair() => (Items[0], 1);                            // G15
    public T? Maybe() => Items[0];                                      // G16
}

public class BaseBag<[NonCopyable] T> where T : struct
{
    protected T[] _items = new T[4];
}

public class DerivedBag<[NonCopyable] T> : BaseBag<T> where T : struct
{
    public T Dup() => _items[0];                                        // G13
}

public class DerivedBagU<T> : BaseBag<T> where T : struct              // G13b unmarked: should be SCA1040
{
}

public sealed class Holder<[NonCopyable] T>(T first) where T : struct  // G20 primary ctor captured
{
    public T Get() => first;                                            // G20b
}

public sealed class Init<[NonCopyable] T> where T : struct
{
    private T _a;
    public T A { init => _a = value; }                                  // G21
    public Init(T a) { _a = a; T b = _a; }                              // G22
}

public static class RT4
{
    public static Lock G01()
    {
        var l = new Lock();
        var b = new Bag<Guard>();
        b.Items[0] = l.Enter();
        Guard g = b.DupA();
        g.Dispose(); b.Items[0].Dispose();
        return l;
    }
}
