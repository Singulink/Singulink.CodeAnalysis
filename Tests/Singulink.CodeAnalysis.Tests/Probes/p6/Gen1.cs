using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Holder
{
    public Guard G;
    public Task<Guard> T;
    public void Deconstruct(out Guard g, out Task<Guard> t) { g = NonCopyable.Move(ref G); t = T; }
}

public sealed class Wrap<[NonCopyable] T>
{
    private T _v = default!;
    public ValueTask<T> TakeVt() => new(NonCopyable.Move(ref _v));
    public (T, int) TakeTuple() => (NonCopyable.Move(ref _v), 1);
    public T[] TakeArray() => new[] { NonCopyable.Move(ref _v) };
    public Task<T> TakeTask() => Task.FromResult(NonCopyable.Move(ref _v));
    public async Task<T> TakeAsync() { await Task.Yield(); return NonCopyable.Move(ref _v); }
    public static implicit operator T(Wrap<T> w) => NonCopyable.Move(ref w._v);
    public static T operator +(Wrap<T> w, int i) => NonCopyable.Move(ref w._v);
    public T this[int i] { get => NonCopyable.Move(ref _v); }
    public T Prop => NonCopyable.Move(ref _v);
    public ref T Ref => ref _v;
    public IEnumerable<T> Iter() { yield return NonCopyable.Move(ref _v); }
    public void Out(out T v) => v = NonCopyable.Move(ref _v);
    public Func<T> Getter() => () => NonCopyable.Move(ref _v);
}

public static class WrapExt
{
    public static T TakeExt<[NonCopyable] T>(this Wrap<T> w) => w.Ref is var _ ? NonCopyable.Move(ref w.Ref) : default!;
    extension<[NonCopyable] T>(Wrap<T> w)
    {
        public T TakeBlock() => NonCopyable.Move(ref w.Ref);
        public T BlockProp => NonCopyable.Move(ref w.Ref);
    }
}

public sealed class Gen1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();

    public async Task Run(Wrap<Task<Guard>> w, Bag<(Guard, Task<Guard>)> hb, Bag<Holder> hold)
    {
        w.Ref = _t;                                              // C00 put a stored task in
        Guard c01 = await await w.TakeVt();                           // C01
        Guard c02 = await w.TakeTuple().Item1;                   // C02
        var (t03, _) = w.TakeTuple();                            // C03
        Guard c03 = await NonCopyable.Move(ref t03);             // C03b
        Guard c04 = await w.TakeArray()[0];                      // C04
        Guard c05 = await await w.TakeTask();                    // C05
        Guard c06 = await await w.TakeAsync();                   // C06
        Task<Guard> t07 = w;                                     // C07
        Guard c07 = await NonCopyable.Move(ref t07);             // C07b
        Guard c08 = await (Task<Guard>)w;                        // C08
        Guard c09 = await (w + 1);                               // C09
        Guard c10 = await w[0];                                  // C10
        Guard c11 = await w.Prop;                                // C11
        foreach (var t in w.Iter()) { Guard c12 = await t; }     // C12
        w.Out(out var t13);
        Guard c13 = await NonCopyable.Move(ref t13);             // C13
        Guard c14 = await w.Getter()();                          // C14
        Guard c15 = await w.TakeExt();                           // C15
        Guard c16 = await w.TakeBlock();                         // C16
        Guard c17 = await w.BlockProp;                           // C17
        Guard c18 = await WrapExt.TakeBlock(w);                  // C18

        // a non-copyable struct that carries a container, bound to T
        hb.Peek().Item2 = _t;                                    // D01
        var (g02, t02) = hb.Take();                              // D02
        Guard d02 = await NonCopyable.Move(ref t02);             // D03
        Guard d04 = await hb.Take().Item2;                       // D04
        if (hb.Take() is (_, var t05)) { Guard d05 = await NonCopyable.Move(ref t05); }   // D05
        hold.Peek().T = _t;                                      // D06
        var (g07, t07b) = hold.Take();                           // D07
        Guard d07 = await NonCopyable.Move(ref t07b);            // D08
        Guard d09 = await hold.Take().T;                         // D09

        // the same without a generic
        (Guard g, Task<Guard> t) tup = (Owner.Enter(), Owner.EnterTaskAsync());
        tup.t = _t;                                              // E01
        var (eg, et) = NonCopyable.Move(ref tup);                // E02
        Guard e03 = await NonCopyable.Move(ref et);              // E03
        var stack = new Stack<(Guard, Task<Guard>)>();
        stack.Push((Owner.Enter(), _t));                         // E04
        var (sg, st) = stack.Pop();                              // E05
        Guard e06 = await NonCopyable.Move(ref st);              // E06
    }
}
