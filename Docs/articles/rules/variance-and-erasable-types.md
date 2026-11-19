<div class="article">

# Variance and Erasable Types

Two things allow a conversion of a container which would otherwise hide its non-copyable type: variance that keeps the type visible, and an erasable type. This page gives both, as the exceptions to [Containers and Conversions](conversions.md).

### At a Glance

- **Variance** is judged by what it does to the values that go through it, not by how the types look.
- **An erasable type** has no base type and no interface that hands out a value of its type arguments, so a container of it may be converted to one of them. The [built-in framework symbol list](framework-symbol-list.md) marks <xref:System.Threading.Tasks.Task`1> as erasable.
- **A class or an interface of your own** is erasable through its marks, with no [symbol list entry](symbol-list-format.md).
- **A type that derives from an erasable type** must stay erasable (SCA1043).

## Variance

A conversion between two constructions of a variant interface or delegate converts what is bound to each variant type parameter, in the direction in which the values go, with no conversion written in the code. So a conversion that only seems to gain the non-copyable type can hide it:

```cs
Action<object> sink = o => _leaked = o;
Action<List<LockGuard>> typed = sink;                // SCA1023: every list that is passed to 'typed' is an object for the sink
Action<IReadOnlyList<LockGuard>> reader = Print;
Action<List<LockGuard>> narrowed = reader;           // OK: the guard type stays visible
```

## Erasable Types

The [symbol list](symbol-list-format.md) marks a generic class or interface as `erasable` when none of its base types and interfaces hands out a value of its type arguments. An erasable type hides what is inside it: a conversion is allowed when every non-copyable type in the converted type sits inside an erasable type.

What comes out of the untyped side is a reference to the erasable object. The only way from there to the result is a cast to the generic type, which gives a stored container:

```cs
// EnterAsync returns a Task<LockGuard>, and EnterValueAsync a ValueTask<LockGuard>
Task plain = EnterAsync();                      // OK: Task<T> is erasable
ShouldBeFaulted(EnterValueAsync().AsTask());    // OK: a helper that takes Task
Array tasks = new Task<LockGuard>[2];           // OK: the guard type sits inside the tasks
IEnumerable<Task> waits = listOfGuardTasks;     // OK
LockGuard g = await (Task<LockGuard>)plain;     // SCA1030: a container from a cast is stored
object boxed = list;                            // SCA1023: List<T> hands out boxed elements through IList
```

Binding a container to a type parameter that is not marked is not a conversion and still gets a warning: generic code can hand the reference back, typed, as the result of a call (`tasks.ToArray()[0]`).

## Your Own Erasable Types

A class or an interface of your own needs no [symbol list entry](symbol-list-format.md), generic or not and sealed or not. Each construction of it is erasable when its marks guarantee what the `erasable` marker only claims:

1. Every type parameter of it that carries a non-copyable type is marked `[NonCopyable]`. A type that is not generic and names the non-copyable type itself, such as a class that implements `IValueTaskSource<LockGuard>`, is already checked code.
2. Every generic base class and generic interface that receives a non-copyable type from it marks the type parameter that receives it. An interface is accepted without the mark if none of its members has a body.
3. No override in the type or its base classes has a return type that mentions a non-copyable type where the overridden member's does not.
4. **No interface of it can be converted to one over `object` by variance.** No reference type that mentions the non-copyable type sits in a covariant position of one of its interfaces.

Variance converts a type argument toward `object` only in a covariant position, with no invariant type parameter on the way. That is an `out` parameter, an `out` inside an `out`, or an `in` inside an `in`, since two contravariant steps make a covariant one:

```cs
interface IProducer<out U> { /* ... */ }

class Producing<[NonCopyable] T> : IProducer<List<T>> { /* ... */ }    // not erasable: IProducer<List<T>> can become IProducer<object>
class Ordered<[NonCopyable] T> : IComparable<Node<T>> { /* ... */ }    // erasable: IComparable<in U> converts only toward more derived types

object a = new Producing<LockGuard>();    // SCA1023
object b = new Ordered<LockGuard>();      // OK
```

A member that boxes a stored `T`, or hands out a container of it untyped, then gets a warning inside the type. A type from another assembly is trusted on the same conditions if that assembly was built with the analyzer. In a type from an assembly that was not built with the analyzer, the marks count as absent: see [Code Not Built With the Analyzer](unchecked-code.md).

```cs
class Pending<[NonCopyable] T> : PendingBase where T : struct { /* ... */ }

PendingBase pending = new Pending<LockGuard>();   // OK: erasable through its marks
```

## Staying Erasable

**A type that derives from an erasable type, or implements one, must stay erasable.** A conversion sees only the type that it is written with, and the object behind it can be of a type that hands out more. So the analyzer checks such a type where it is declared, for every way in which it can be constructed.

It gets a warning (SCA1043) if it adds a base class or an interface with bodies that are not checked for the non-copyable type that it receives, or an interface with a covariant position of the kind that rule 4 above rules out.

- A type parameter that is not constrained to `struct` counts as a reference type there.
- This holds as well for a type that derives from one that the [built-in framework symbol list](framework-symbol-list.md) marks, such as <xref:System.Threading.Tasks.Task`1>.

```cs
class Bag<[NonCopyable] T> { }

// With a list of guards as T, the object could be converted on to IProducer<object>:
class SortedBag<[NonCopyable] T> : Bag<T>, IProducer<T> { }                   // SCA1043

class GuardBag<[NonCopyable] T> : Bag<T>, IProducer<T> where T : struct { }   // OK: variance needs a reference type
```

</div>
