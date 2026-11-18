<div class="article">

# Containers and Conversions

A container that is converted to a type which no longer mentions the non-copyable type is reported, because the code that receives it cannot be
checked. <xref:System.Array> and the non-generic collection interfaces, which hand out boxed copies, are `SCA1022`. Everything else, such as `object`, a
base class or an interface that is not a collection, is `SCA1023`, and so is a container that is bound to a type parameter that is not marked.

## Examples

```cs
IReadOnlyList<LockGuard> typed = list;       // OK: the guard type is still visible
IEnumerable untyped = list;                  // SCA1022
object boxed = list;                         // SCA1023
LockGuard[] clone = (LockGuard[])guards.Clone(); // SCA1022: Array.Clone is not listed
int length = guards.Length;                  // OK: Array.Length is listed
ArgumentNullException.ThrowIfNull(list);     // OK: listed
```

## Conversions With Nothing Written

A member on the list may receive a container as its receiver or an argument. `SCA1023` has its own ID so that it can be turned down alone: containers
reach `object` through logging and assertions all the time.

The same is reported where the language converts with no expression: at an override whose return type mentions a non-copyable type that the
return type of the overridden member does not, at a method group that is converted to a delegate whose return type or parameter types hide
one, at a delegate that is created from another delegate, and at a `foreach` whose variable has a type that hides the elements.

```cs
public override LockGuard[] GetItems() => new LockGuard[4];   // SCA1023: the base member returns object
Func<object> make = MakeGuards;                               // SCA1023: MakeGuards returns LockGuard[]
var wrapped = new Func<object>(makeGuards);                   // SCA1023: makeGuards is a Func<LockGuard[]>
foreach (IList list in listOfGuardLists) { }                  // SCA1022: each List<LockGuard> becomes an IList
```

Two things allow a conversion that would otherwise hide the type: variance that keeps the type visible, and a type that the list marks
`erasable`, as <xref:System.Threading.Tasks.Task`1> is, so that `Task plain = EnterTaskAsync();` is allowed. Both are in
[Variance and erasable types](variance-and-erasable-types.md).

## Boxes

Boxing or converting a value of a non-copyable type itself is a matter of values:

- **Boxing a new value is a move into the box.** Nobody else holds the value, and in the box it is one object that every holder of the reference
  shares. Calls through an interface run on the box itself. A value of a marked type parameter is the exception, since a container can be
  bound to it: see [Marking a type parameter](../guides/marking-a-type-parameter.md).
- **A generic struct that is non-copyable only through its type arguments cannot be boxed** (`SCA1023`), not even a new one: a tuple, a
  `ValueTask<LockGuard>`, a `List<LockGuard>.Enumerator`. Its own code is not checked, and what its interfaces give on the box, such as
  `ITuple[0]`, are boxed copies. A `LockGuard?` boxes as the guard itself and is not affected, and neither is a struct whose type parameter is
  marked `[NonCopyable]`.
- **Boxing a stored value copies it** and is reported (`SCA1004`), whether a conversion, `as` or a pattern does it: in
  `guard is IDisposable d`, `d` is a boxed copy. A bare type test (`guard is IDisposable`) boxes nothing that can be used.
- **Every way back out of a box is reported** (`SCA1001`): a cast, `as`, a pattern that declares a variable or looks into the value with a
  member that is not `readonly`, an assignment from `dynamic`. A bare type test (`box is LockGuard`) copies nothing.
- **A union is a box.** A `union` declaration (C# 15) makes a struct with one field of type `object`: its constructor boxes the case
  value, and a copy of the union shares the box. So a pattern on a union takes a copy out of the box at every match (`SCA1001`), also
  on a union that a call has just returned, which can share its box with one that is stored. That goes for a union in every form:
  a nullable of one (`GuardOrCode?`), and every struct or class that implements `System.Runtime.CompilerServices.IUnion` or carries
  `System.Runtime.CompilerServices.UnionAttribute`, as one written by hand does. Use the value in place through the box, as in
  `((IDisposable)result.Value!).Dispose();`. A bare type test copies nothing.
- **A type pattern between a struct and a type parameter goes through a box,** in both directions. From a value of a type parameter
  to a struct the compiler boxes and unboxes: `item is Lease (var read, var write)` on a marked `T` takes a copy out of a box
  (`SCA1001`). From a struct to a type parameter the result can be the box itself: `guard is T t` boxes a copy of a stored guard
  (`SCA1004`). The bare tests `item is LockGuard` and `guard is T` copy nothing.

```cs
IDisposable held = myLock.Enter();           // OK: the guard moves into the box
held.Dispose();                              // runs on the box
object copy = guard;                         // SCA1004: boxes a copy of a stored value
LockGuard back = (LockGuard)held;            // SCA1001: a copy out of the box
object pair = (myLock.Enter(), 1);           // SCA1023: the code of the tuple is not checked
```

</div>
