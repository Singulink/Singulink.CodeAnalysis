<div class="article">

# Moving Values

A guard is handed on by moving it, not by copying it. Most of the time the move happens by itself, at the last use of a variable. Where it cannot, <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> does it by hand.

### At a Glance

| You have | To hand it on |
|---|---|
| A local or a by-value parameter that you do not use again | Assign it or pass it: the last use moves it |
| A local that you still need afterwards | Read what you need first, then hand it on, or move it out |
| A field, an array element, a `ref` parameter, a captured variable | Move it out with `NonCopyable.Move(ref x)` |
| A `using` variable or a `readonly` field | You cannot: something else still owns the value |

## The Last Use Moves

Nothing can use a local after its last use, so the analyzer treats that last use as a move:

```cs
var guard = myLock.Enter();
int id = guard.Id;
_held = guard;                      // OK: the last use
Log(id);
```

A `catch` or a `finally` that can still run also uses the variable after that point. So does the next pass of a loop, for a variable declared outside the loop. A handover in either place is not the last use: see [Cleaning Up When Something Throws](cleanup-when-throwing.md).

> [!NOTE]
> Some variables are never moved at their last use, because something else can still reach them, for example a captured variable or a `using` variable. All of them: [Variables That Are Never Moved From](../rules/last-use.md#variables-that-are-never-moved-from).

## Moving by Hand

<xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> takes the value out of a variable, a field or an array element and leaves the default value behind. Its result is a new value, so it can go anywhere once:

```cs
TakeGuard(NonCopyable.Move(ref _guard));    // OK: moved out, and the field is left empty
_guard.Dispose();                           // OK: disposes the default value that the move left
LockGuard first = NonCopyable.Move(ref _slots[0]);   // OK: the element is left empty
```

A second use of the variable hits a default value, not a second guard. That is why a guard should do nothing in `Dispose` when it holds nothing: see [What Is Guaranteed](../start/what-it-does.md#what-is-guaranteed).

#### What Cannot Be Moved From

A `using` variable and a `readonly` field cannot be passed by `ref`, so they cannot be moved from: something else still owns the value.

#### Nullable Warnings After a Move

The compiler's null checks treat a moved-from variable of a reference type, such as a `Task<LockGuard>`, as possibly null afterwards. Declare it with `var` or as nullable. In a generic collection with no `where T : struct`, a move out of a slot, as in `NonCopyable.Move(ref _items[index])`, gives the compiler's CS8601: declare the slots as `T?[]`, or suppress CS8601 at the move.

The exact rules: [Move Helper](../rules/moved-from.md#move-helper).

## Moving a Task or a List

A guard has one holder by construction, so it can be moved out of anything. A task or a list is a reference that other variables may hold too, so clearing one variable proves nothing. So the analyzer lets you move such a container only out of a local that the method owns: one that holds what the method created or got from a call:

```cs
var pending = myLock.EnterAsync();
LockGuard a = await NonCopyable.Move(ref pending);   // OK: the method's own

LockGuard b = await NonCopyable.Move(ref _cached);   // SCA1033 and SCA1030: whoever else has the task can await it too
```

A value task is different: `ValueTask<LockGuard>` is a struct that holds the guard, so it is non-copyable itself, and it can be moved out of a field like a guard:

```cs
using var held = await NonCopyable.Move(ref _pending);   // OK: a value task is moved out of the field
```

The exact rules: [Moving a Container](../rules/moves-of-containers.md#moving-a-container).

## Writing Your Own Move

A project that declares the attributes itself has no `NonCopyable.Move`, and writes its own: see [Writing a Move Method](writing-a-move.md).

## Turning the Move at the Last Use Off

A project that wants every handover written out can turn the move at the last use off. Then only a move by hand hands a value over: see [Turning Off the Move on the Last Use](../rules/msbuild-properties.md#turning-off-the-move-on-the-last-use).

</div>
