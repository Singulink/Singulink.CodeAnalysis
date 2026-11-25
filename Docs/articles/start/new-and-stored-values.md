<div class="article">

# New and Stored Values

Every warning of the analyzer comes down to one question: does something else still hold this value? This page explains the two answers. With them you can read most warnings and see how to fix them.

### Problem in One Example

A struct is copied every time it is assigned or passed by value. For most structs that does no harm. For a lock guard it does:

```cs
var guard = myLock.Enter();     // one guard, which holds the lock
var copy = guard;               // now two guards hold the same lock

guard.Dispose();                // SCA1016: releases the lock, and 'copy' still holds it
copy.Dispose();                 // releases it again
```

Mark the struct with `[NonCopyable]` and you get a warning for every such copy. To tell a copy from a legitimate handover, the analyzer sorts every value into one of two kinds: **new** or **stored**.

## New Values

A new value is one that nothing else holds yet. It can go anywhere, once: into a variable, a field, a parameter, a tuple, an array or a collection.

| A new value is | For example |
|---|---|
| A value that is created | `new LockGuard(...)`, `default` |
| A guard returned by a call or an operator | `myLock.Enter()` |
| The result of a move | `NonCopyable.Move(ref _guard)` |
| A local or a by-value parameter at its last use | see [Last Use of a Variable](#last-use-of-a-variable) below |

```cs
_guard = myLock.Enter();                    // OK: the result of a call is new
guards.Add(myLock.Enter());                 // OK: the new guard moves into the list
TakeGuard(NonCopyable.Move(ref _guard));    // OK: moved out, and the field is left empty
```

## Stored Values

A stored value is one that is read from a place that goes on holding it: a local, a parameter, a field, an array element, or what a reference refers to. Reading it **by value** makes a copy, and the copy is what you get a warning for.

You can still work with a stored value. Use it where it is, or refer to it:

```cs
_guard.Dispose();                   // OK: called on the field itself
TakeGuard(ref _guard);              // OK: passed by reference ('in' and 'ref readonly' work too)
ref LockGuard alias = ref _guard;   // OK: a reference, not a copy

LockGuard copy = _guard;            // SCA1001: a copy of the field
TakeGuard(_guard);                  // SCA1003: passed by value
return _guard;                      // SCA1002: returns a copy
```

> [!NOTE]
> Behind a read-only reference, such as an `in` parameter or a `readonly` field, the compiler runs a member that is not `readonly` on a hidden copy. A `Dispose` there releases the copy and leaves the original held, so you get a warning (SCA1006). See [Copies You Cannot See](../guides/hidden-copies.md).

## Last Use of a Variable

A local or a by-value parameter is stored up to the place where it is used for the last time. There it becomes a new value: nothing uses the variable afterwards, so handing the value on leaves no second copy behind.

```cs
var guard = myLock.Enter();
Log(guard.Id);
_held = guard;                      // OK: 'guard' is not used again, so the guard moves into the field
```

Use the variable once more, and the same line is a copy. The warning goes to the use after it, where the code goes wrong (SCA1016):

```cs
var guard = myLock.Enter();
_held = guard;                      // a copy, since 'guard' is read again below
Log(guard.Id);                      // SCA1016: 'guard' is used after it was handed over
```

The fix is usually to read what you need first, so that the handover is the last use:

```cs
var guard = myLock.Enter();
int id = guard.Id;
_held = guard;                      // OK: the last use
Log(id);
```

A `catch`, a `finally` or an exception filter that uses the variable counts as a later use too, and the message of the warning says so. [Cleaning Up When Something Throws](../guides/cleanup-when-throwing.md) shows the patterns that work with a `catch`: store the value at the end of the `try` block, after everything that can throw, or hand it to a member such as `List<T>.Add` after the `try` statement.

Some variables never become new at their last use, because something else can still reach them. For example:

- a variable that a lambda or a local function captures;
- a `using` variable, which the end of its scope still uses.

The exact rule, with every case: [Last Use of a Variable](../rules/last-use.md).

> [!TIP]
> Where the handover cannot be the last use, as for a field, an array element, or a variable that is used again afterwards, move the value out with <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>. It hands the value over and leaves the default value behind. See [Moving Values](../guides/moving-values.md).

## Containers

A list, an array or a task of guards is a **container**. Copying a container is fine: it is a reference, and no guard is copied. What counts is reading a guard *out* of a container by value, while the container still holds it.

```cs
var guards = new List<LockGuard>();
guards.Add(myLock.Enter());                         // OK: a new guard moves in

LockGuard first = guards[0];                        // SCA1030: the list still holds the guard
CollectionsMarshal.AsSpan(guards)[0].Dispose();     // OK: used in place, through a span
```

`CollectionsMarshal.AsSpan(list)` gives a span over the list's own storage, so you can refer to an element instead of copying it.

A task works the same way. A task in a variable is stored until its last use, so "start now, await later" needs nothing more:

```cs
var pending = myLock.EnterAsync();                  // start now
using var held = await pending;                     // OK: 'pending' is not used again
```

## Next Steps

Here is where to go from here:

- [Fixing Common Warnings](fixing-common-warnings.md) - The handful of fixes that cover most warnings.
- [Moving Values](../guides/moving-values.md) - Handing a value on when the last use is not enough.
- [New and Stored Values in Detail](../rules/new-and-stored-values.md) - Every case, including a few values that look new but are stored. Read it when a warning surprises you.

</div>
