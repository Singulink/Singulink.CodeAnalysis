<div class="article">

# Fixing Common Warnings

Most warnings are fixed in one of a few ways. Find the situation that matches your code: each one shows the warning and the fix side by side.

## Collections

#### Element of a List

Read it through a span. `CollectionsMarshal.AsSpan(list)` gives a span over the list's own storage, so you can refer to an element instead of copying it.

```cs
LockGuard first = guards[0];                              // SCA1030: a copy out of the list
guards[0].Dispose();                                      // SCA1030: disposes a copy

CollectionsMarshal.AsSpan(guards)[0].Dispose();           // OK: in place
ref LockGuard inPlace = ref CollectionsMarshal.AsSpan(guards)[0];
```

#### Loop Over the Elements

A `foreach` with a by-value variable copies each element, of a list and of an array alike. Declare a `ref` variable over a span of them.

```cs
foreach (var guard in guards) guard.Dispose();            // SCA1030: each 'guard' is a copy out of the list
foreach (var guard in array) guard.Dispose();             // SCA1001: each 'guard' is a copy of an element

foreach (ref var guard in CollectionsMarshal.AsSpan(guards)) guard.Dispose();   // OK
foreach (ref var guard in array.AsSpan()) guard.Dispose();                      // OK
```

#### Value in a Dictionary

Take it out with the member that removes it. To release every value, take a snapshot of the keys first.

```cs
LockGuard guard = byKey[key];                            // SCA1030: a copy, and the dictionary still has the guard

if (byKey.Remove(key, out var taken)) taken.Dispose();   // OK: taken out

int[] keys = [.. byKey.Keys];                            // OK: the keys only
foreach (int k in keys) { if (byKey.Remove(k, out var each)) each.Dispose(); }
```

More on collections, and on what the analyzer knows about each one: [Lists, Dictionaries and Spans](../guides/collections.md).

## Fields and Variables

#### Field That Is Handed On

Move it out, which leaves the field empty.

```cs
return _guard;                                            // SCA1002
return NonCopyable.Move(ref _guard);                      // OK

Take(_guard);                                             // SCA1003
Take(NonCopyable.Move(ref _guard));                       // OK
```

#### Variable That Is Used After It Was Handed Over

Read what you need first. A variable is a new value where it is used for the last time, so the hand-off has to be that last use. The warning is at the use after the hand-off.

```cs
_held = guard;                                            // a copy, since 'guard' is read again below
Log(guard.Id);                                            // SCA1016: used after it was handed over

int id = guard.Id;
_held = guard;                                            // OK: the last use
Log(id);
```

## Declarations

#### Guard Through an In Parameter

Behind an `in` parameter, a member that is not `readonly` runs on a copy. Take the guard by `ref` instead:

```cs
static void Close(in LockGuard guard) => guard.Dispose();     // SCA1006: disposes a copy
static void CloseByRef(ref LockGuard guard) => guard.Dispose(); // OK
```

#### Property of a Guard Type

The getter of an auto-property returns a copy of its backing field. Use a field:

```cs
public LockGuard Guard { get; set; }              // SCA1011: the getter hands out a copy
private LockGuard _guard;                         // OK: a field
```

#### Struct That Holds a Guard

A struct is copied with everything in it, so a struct that holds a guard has to be marked too:

```cs
struct Holding { LockGuard _guard; }                  // SCA1010: a struct that holds a guard must be marked too

[NonCopyable]
struct MarkedHolding { LockGuard _guard; }            // OK
```

## More Cases

Three more cases have a guide of their own:

- **A callback that works on a guard**, which needs a delegate with a `ref` parameter: see [Callbacks](../guides/tasks.md#callbacks).
- **A constructor that validates and then takes**, which needs a `[MovedFrom] ref` parameter: see [Constructors That Validate and Then Take](../guides/cleanup-when-throwing.md#constructors-that-validate-and-then-take).
- **A task that a waiter completes later**, made with a `TaskCompletionSource`: see [Making a Task for a Waiter](../guides/tasks.md#making-a-task-for-a-waiter).

> [!NOTE]
> A few shapes of correct code still get a warning, because the analyzer follows a value through one method at a time. [Correct Code That Still Gets a Warning](../guides/correct-code-with-warnings.md) shows each one, with the clean form or the suppression to use.

## Next Steps

Here is where to go from here:

- [New and Stored Values](new-and-stored-values.md) - The idea behind every one of these fixes.
- [Why Am I Seeing This?](why-am-i-seeing-this.md) - If the warnings came with a library that you use.
- [All Diagnostics](../all-diagnostics.md) - The article for every ID.

</div>
