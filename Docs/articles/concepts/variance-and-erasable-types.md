<div class="article">

# Variance and Erasable Types

Two things that allow a conversion of a container which would otherwise hide its non-copyable type. These belong to [Containers and conversions](conversions.md).

## Variance

**Variance is judged by what it does.** A conversion between two constructions of a variant interface or delegate converts what is bound to
each variant type parameter, in the direction in which the values go, with no conversion written. So a conversion that only seems to gain the
non-copyable type can hide it:

```cs
Action<object> sink = o => _leaked = o;
Action<List<LockGuard>> typed = sink;                // SCA1023: every list that is passed to 'typed' is an object for the sink
Action<IReadOnlyList<LockGuard>> reader = Print;
Action<List<LockGuard>> narrowed = reader;           // OK: the guard type stays visible
```

## Erasable Types

**Erasable types.** The list marks a generic class or interface as `erasable` when no base type and no interface of it hands out a value of
its type arguments. The built-in list marks <xref:System.Threading.Tasks.Task`1>. An erasable type hides what is inside it: a conversion is allowed when every
non-copyable type in the converted type sits inside an erasable type. What comes out of the untyped side is a reference to the erasable object,
and the only way from there to the result is a cast to the generic type, which gives a stored container:

```cs
Task plain = EnterTaskAsync();                  // OK: Task<T> is erasable
ShouldBeFaulted(EnterAsync().AsTask());         // OK: a helper that takes Task
Array tasks = new Task<LockGuard>[2];           // OK: the guard type sits inside the tasks
IEnumerable<Task> waits = listOfGuardTasks;     // OK
LockGuard g = await (Task<LockGuard>)plain;     // SCA1030: a container from a cast is stored
object boxed = list;                            // SCA1023: List<T> hands out boxed elements through IList
```

Binding a container to a type parameter that is not marked is not a conversion and stays reported: generic code can hand the reference back,
typed, as the result of a call (`tasks.ToArray()[0]`).

A class or an interface of your own needs no list entry, generic or not and sealed or not. It is erasable, as it is constructed, when its
marks guarantee what the marker only claims:

- every type parameter of it that carries a non-copyable type is marked `[NonCopyable]`. A type that is not generic and names the
  non-copyable type itself, such as a class that implements `IValueTaskSource<LockGuard>`, is checked code as it is;
- every generic base class and generic interface that receives a non-copyable type from it marks the type parameter that receives it, where
  an interface is accepted without the mark if none of its members has a body;
- no override in the type or its base classes has a return type that mentions a non-copyable type where the overridden member's does not;
- none of its interfaces has a reference type that mentions the non-copyable type in a position that is covariant in effect: `out`, `out`
  inside `out`, or `in` inside `in`, with no invariant type parameter on the way. Only from there does variance convert the interface to
  one over `object`, whose members hand the container out untyped or take a delegate that receives it untyped. A contravariant position
  converts toward more derived types only, so an `IComparable<Node<T>>` is no obstacle.

A member that boxes a stored `T`, or hands out a container of it untyped, is then reported inside the type. A type from another assembly is
trusted on the same conditions.

```cs
class Pending<[NonCopyable] T> : PendingBase where T : struct { /* ... */ }

PendingBase pending = new Pending<LockGuard>();   // OK: erasable through its marks
```

**A type that derives from an erasable type, or implements one, must stay erasable.** A conversion sees only the type that it is written with,
and the object behind it can be of a type that hands out more. So such a type is checked where it is declared, for every way in which it can
be constructed, and reported (`SCA1043`) if it adds a base class or an interface with bodies that are not checked for the non-copyable type
that it receives, or an interface with such a position. A type parameter that is not constrained to `struct` counts as a reference type
there. This holds as well for a type that derives from one that the list marks, such as <xref:System.Threading.Tasks.Task`1>.

```cs
class Bag<[NonCopyable] T> { }

class SortedBag<[NonCopyable] T> : Bag<T>, IProducer<T> { }                   // SCA1043: with a list of guards as T, the object
                                                                              // could be converted on to IProducer<object>

class GuardBag<[NonCopyable] T> : Bag<T>, IProducer<T> where T : struct { }   // OK: variance needs a reference type
```

</div>
