using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class VBag<[NonCopyable] T> { }
public interface IProducer<out U> { U Get(); }
public interface IConsumer<in U> { void Put(U u); }

public class Node<[NonCopyable] T> : VBag<T>, IComparable<Node<T>>               // N01 SCA1043 (the implementer's noted FP)
{
    public int CompareTo(Node<T>? other) => 0;
}
public class Node2<[NonCopyable] T> : VBag<T>, IEquatable<Node2<T>>               // N02 IEquatable is invariant: OK
{
    public bool Equals(Node2<T>? other) => false;
}
public class Node3<[NonCopyable] T> : VBag<T>, IConsumer<Action<T>>               // N03 in of in = covariant: SCA1043 expected
{
    public void Put(Action<T> a) { }
}
public class Node4<[NonCopyable] T> : VBag<T>, IConsumer<T>                        // N04 contravariant only: effective variance says OK?
{
    public void Put(T a) { }
}
public class Node5<[NonCopyable] T> : VBag<T>, IEnumerable<Node5<T>>               // N05 covariant self: SCA1043
{
    public IEnumerator<Node5<T>> GetEnumerator() => null!;
    IEnumerator IEnumerable.GetEnumerator() => null!;
}

class VarUse
{
    static void Sink(object o) { }
    async Task M(List<Guard> list, IEnumerable<Task<Guard>> tasks, Task<Guard>[] arr)
    {
        IEnumerable<Task> plain = tasks;                                             // O01 OK erasable
        IEnumerable<object> objs = tasks;                                            // O02 OK? Task<Guard> -> object erasable
        Action<List<Guard>> typed = Sink;                                            // O03 SCA1023 (method group param)
        Action<object> sink = Sink;
        Action<List<Guard>> typed2 = sink;                                           // O04 SCA1023
        Action<IReadOnlyList<Guard>> reader = x => { };
        Action<List<Guard>> narrowed = reader;                                       // O05 OK
        Func<IEnumerable<Task<Guard>>> mk = () => new List<Task<Guard>>();
        Func<IEnumerable<Task>> mk2 = mk;                                            // O06 OK (erasable)
        IComparer<Task> cmp = null!;
        IComparer<Task<Guard>> cmp2 = cmp;                                           // O07 OK? values flow Task<Guard> -> Task: erasable
        Action<Task> at = t => { };
        Action<Task<Guard>> at2 = at;                                                // O08 OK erasable
        IConsumer<Node<Guard>> c1 = null!;
        IComparable<Node<Guard>> cmpn = new Node<Guard>();                           // O09 OK
        object on = new Node<Guard>();                                               // O10 erasable? Node gets 1043
        IComparable<VBag<Guard>> wider = null!;
        IComparable<Node<Guard>> nar = wider;                                        // O11 values flow Node -> VBag : erasable OK
        Task<Guard[]> ta = null!;
        Task plainArr = ta;                                                          // O12 OK erasable
        IEnumerable<Task<Guard>[]> nested = null!;
        IEnumerable<Array> nestedArr = nested;                                       // O13 ? Task<Guard>[] -> Array: elements are erasable Task<Guard>s
        IEnumerable<IEnumerable> nestedE = nested;                                   // O14 ?
    }
}
