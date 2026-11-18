using System;
using System.Collections;
using System.Collections.Generic;
using Singulink.CodeAnalysis;
using static Src;

abstract class PendingBase { public abstract object Items { get; } public virtual object Get() => new object(); }
sealed class Pending<[NonCopyable] T> : PendingBase where T : struct
{
    private T[] _items = new T[4];
    public override T[] Items => _items;                         // AD1 covariant return: no conversion written
    public override T[] Get() => _items;                         // AD2 covariant return
}

interface IProducer<out U> { U Take(); }
sealed class Holder<[NonCopyable] T> : IProducer<T>
{
    private T _value = default!;
    public T Take() => NonCopyable.Move(ref _value);             // AD3 a move: OK
}

abstract class BaseCtor { protected BaseCtor(object o) { } }
sealed class ViaCtor<[NonCopyable] T> : BaseCtor { public ViaCtor(T[] items) : base(items) { } }        // AD4 report?
sealed class ViaPrimary<[NonCopyable] T>(T[] items) : BaseCtor(items) { }                                 // AD5 report?

class P11
{
    void Use(Pending<Guard> p, Holder<List<Guard>> h)
    {
        PendingBase b = p;                                       // AE1 reported today; allowed under the proposal
        IList il = (IList)b.Get();                               // AE2 no report: object -> IList
        ((IDisposable)il[0]!).Dispose();                         // AE3 disposes a boxed copy of a guard

        IProducer<object> po = h;                                // AE4 reported today; allowed under the proposal
        var untyped = (IList)po.Take();                          // AE5 no report
        ((IDisposable)untyped[0]!).Dispose();                    // AE6 disposes a boxed copy
    }
}
