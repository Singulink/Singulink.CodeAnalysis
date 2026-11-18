using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public interface IIn<in T> { void Put(T x); }
public interface IOut<out T> { T Get(); }
public class VB<[NonCopyable] T> { }
public class W1<[NonCopyable] T> : VB<T>, IIn<IIn<W1<T>>> { void IIn<IIn<W1<T>>>.Put(IIn<W1<T>> x) { } }         // V01 in-in covariant: report
public class W2<[NonCopyable] T> : VB<T>, IIn<IOut<W2<T>>> { void IIn<IOut<W2<T>>>.Put(IOut<W2<T>> x) { } }       // V02 in-out contravariant: OK
public class W3<[NonCopyable] T> : VB<T>, IOut<IIn<W3<T>>> { public IIn<W3<T>> Get() => null!; }                  // V03 out-in contravariant: OK
public class W4<[NonCopyable] T> : VB<T>, IOut<IOut<W4<T>>> { public IOut<W4<T>> Get() => null!; }                // V04 out-out covariant: report
public class W5<[NonCopyable] T> : VB<T>, IOut<List<W5<T>>> { public List<W5<T>> Get() => null!; }                 // V05 invariant on the way: OK
public class W6<[NonCopyable] T> : VB<T>, IIn<IIn<IIn<W6<T>>>> { void IIn<IIn<IIn<W6<T>>>>.Put(IIn<IIn<W6<T>>> x) { } } // V06 in-in-in contravariant: OK
public class W7<[NonCopyable] T> : VB<T>, IOut<Func<W7<T>>> { public Func<W7<T>> Get() => null!; }                 // V07 out-out(delegate) covariant: report
public class W8<[NonCopyable] T> : VB<T>, IOut<T> where T : class { public T Get() => null!; }                      // V08 T itself covariant, reference: report?
