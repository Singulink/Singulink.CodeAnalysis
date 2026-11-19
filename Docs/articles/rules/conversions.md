<div class="article">

# Containers and Conversions

A container that is converted to a type which no longer mentions the non-copyable type gets a warning, because the code that receives it cannot be checked. This page gives the rule. Boxing a value of a non-copyable type itself is under [Boxes and Unions](boxes.md).

### At a Glance

| Conversion of a container | Warning |
|---|---|
| To <xref:System.Array> or a non-generic collection interface, which hand out boxed copies | SCA1022 |
| To anything else that hides the non-copyable type: `object`, a base class, an interface that is not a collection | SCA1023 |
| Binding it to a type parameter that is not marked | SCA1023 |
| To a type that still shows the non-copyable type, such as `IReadOnlyList<LockGuard>` | OK |
| Through variance that keeps the type visible, or to a base type of an `erasable` type | OK: see [Variance and Erasable Types](variance-and-erasable-types.md) |

## Examples

```cs
IReadOnlyList<LockGuard> typed = list;       // OK: the guard type is still visible
IEnumerable untyped = list;                  // SCA1022
object boxed = list;                         // SCA1023
LockGuard[] clone = (LockGuard[])guards.Clone(); // SCA1022: Array.Clone is not on the symbol list
int length = guards.Length;                  // OK: Array.Length is on the symbol list
ArgumentNullException.ThrowIfNull(list);     // OK: on the symbol list
```

A member on the [symbol list](symbol-list-format.md) may receive a container as its receiver or an argument, and that does not count as a conversion. That is why `ThrowIfNull(list)` is fine, even though its parameter is an `object`.

> [!TIP]
> SCA1023 has its own ID so that it can be turned down alone: containers reach `object` through logging and assertions all the time.

## Conversions With Nothing Written

You get the same warning where the language converts a container with no conversion written in the code:

- at an override whose return type mentions a non-copyable type that the return type of the overridden member does not;
- at a method group that is converted to a delegate whose return type or parameter types hide one;
- at a delegate that is created from another delegate;
- at a `foreach` whose variable has a type that hides the elements.

```cs
public override LockGuard[] GetItems() => new LockGuard[4];   // SCA1023: the base member returns object
Func<object> make = MakeGuards;                               // SCA1023: MakeGuards returns LockGuard[]
var wrapped = new Func<object>(makeGuards);                   // SCA1023: makeGuards is a Func<LockGuard[]>
foreach (IList list in listOfGuardLists) { }                  // SCA1022: each List<LockGuard> becomes an IList
```

## What Allows a Conversion

Two things allow a conversion that would otherwise hide the type: variance that keeps the type visible, and a type that the [symbol list](symbol-list-format.md) marks `erasable`. <xref:System.Threading.Tasks.Task`1> is erasable, so `Task plain = EnterAsync();` is allowed. Both are in [Variance and Erasable Types](variance-and-erasable-types.md).

</div>
