using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

interface IUntyped6 { object? Get(); }
interface IConsumer6<in U> { void Put(U u); }
interface IProducer6<out U> { U Take(); }
abstract class PendingBase6 { }

sealed class Pending6<[NonCopyable] T> : PendingBase6, IConsumer6<Action<List<T>>> where T : struct
{
    private List<T> _list = new();
    public void Put(Action<List<T>> a) => a(_list);                                // G00 stored container to a delegate
}

sealed class Pending6b<[NonCopyable] T> : PendingBase6, IUntyped6 where T : struct
{
    private T _v;
    [RequiresCopyable] object? IUntyped6.Get() => _v;                              // G01 SCA1040 expected
}

sealed class Pending6c<[NonCopyable] T> : PendingBase6, IUntyped6 where T : struct
{
    private T _v;
    public object? Get() => Helper();                                              // G02 SCA1021 expected
    [RequiresCopyable] private object? Helper() => _v;
}

class Bag6<[NonCopyable] T> { }
class Sub6a<[NonCopyable] T> : Bag6<T>, IConsumer6<Action<T>> { public void Put(Action<T> a) { } }        // G10 SCA1043? (double contravariance with T a reference)
class Sub6b<[NonCopyable] T> : Bag6<T>, IConsumer6<Action<List<T>>> where T : struct { public void Put(Action<List<T>> a) { } }  // G11

class P6
{
    private List<Guard> _l = new();
    private List<List<Guard>> _ll = new();

    void M()
    {
        var p = new Pending6<Guard>();
        object o = p;                                                              // G20 allowed as erasable?
        object? leaked = null;
        ((IConsumer6<Action<object>>)o).Put(x => leaked = x);                      // G21 the list arrives as object
        object? copy = ((IList)leaked!)[0];                                        // G22 boxed copy

        if (_l is object lo) { }                                                   // G30 container to object by pattern
        if (_l is IEnumerable le) { }                                              // G31
        switch (_l) { case IList li: break; }                                      // G32
        _ = _l switch { ICollection lc => 1, _ => 0 };                             // G33
        foreach (IList x in _ll) { }                                               // G34 element conversion in foreach
        foreach (object x in _ll) { }                                              // G35
        (object a, int b) = (_l, 1);                                               // G36 deconstruction to object
        var anon = new { L = _l };
        object ao = anon;                                                          // G37 anonymous type to object
        string s = "x" + _l;                                                       // G38 concatenation
        object[] arr = { _l };                                                     // G39
        object[] arr2 = [_l];                                                      // G40
        List<object> lo2 = [_l];                                                   // G41
        IEnumerable<object> e3 = _ll;                                              // G42 covariance List<List<Guard>> -> IEnumerable<object>
        Func<object> fo = () => _l;                                                // G43
        object cond = true ? _l : "x";                                             // G44
        dynamic dyn = _l;                                                          // G45
        Task t = Owner.EnterTaskAsync();                                           // G46 fine
        object ot = (object)Owner.EnterTaskAsync();                                // G47 fine
        Task<Task<Guard>> tt = Task.FromResult(Owner.EnterTaskAsync());
        Task tt2 = tt;                                                             // G48 fine
        IEnumerable<Task> it = new List<Task<Guard>>();                            // G49 fine
        IEnumerable<object> it2 = new List<Task<Guard>>();                         // G50 fine?
    }
}
