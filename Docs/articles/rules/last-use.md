<div class="article">

# Last Use of a Variable

A variable that is not used again leaves no second copy behind. So where a method uses a variable for the last time, the variable is a new value instead of a stored one. There, every rule allows what it allows for a new value: you can assign it, pass it by value, put it in a tuple or a box, use it in a `using` statement, or match a pattern on it.

### At a Glance

- **Which variables:** a local or a by-value parameter of a non-copyable type, and a local that holds a container which is the method's own.
- **The last use:** a use after which no path, an exception path included, uses the variable again before the variable is assigned as a whole.
- **Never moved from:** fields, elements, references, captured and `using` variables, views, and a variable that a reference can outlive.
- **Turning it off:** a project can turn the move at the last use off: see [Turning Off the Move on the Last Use](msbuild-properties.md#turning-off-the-move-on-the-last-use).

## Example

```cs
LockGuard guard = myLock.Enter();
_guard = guard;                          // OK: the last use of 'guard'

var pending = locks.LockAsync(key);      // a ValueTask<KeyLock>
if (!pending.IsCompletedSuccessfully)
    return WaitAsync(pending);           // OK: the last use on this path
using var held = pending.Result;         // OK: the last use on this path

var other = locks.LockAsync(key);
var first = other.Result;                // SCA1030: 'other' is read again below
var second = other.Result;               // OK: the last use of 'other'
```

## Which Variables

#### Variables of a Non-copyable Type

A local or a by-value parameter whose type is a non-copyable type. That includes:

- a `ValueTask<LockGuard>`, a `LockGuard?` and a tuple;
- a parameter of a lambda or of a setter;
- a variable that a pattern, a deconstruction, an `out` argument or a `foreach` declares.

A part of such a variable is a new value at the last use of the variable too:

```cs
var pair = (guard: myLock.Enter(), id: 1);
_guard = pair.guard;                     // OK: the last use of 'pair'
```

#### Locals That Hold a Container

A local that holds a container of a reference type, such as a `Task<LockGuard>` or a `List<LockGuard>`, has to meet a stricter test. It is a new container at its last use only if it passes the **stricter** of the two tests of [a local that the method owns](owned-locals.md). That test is the [first list](owned-locals.md#what-any-local-may-do), whatever the local holds. The extra uses that a local of guards may have and still be returned do not count here.

On top of those uses, the local may be awaited, and a view of it may be awaited or read where it is made, as in `await pending.ConfigureAwait(false)`. At its last use, each of these then reads from a new container:

```cs
var pending = myLock.EnterAsync();
LockGuard a = await pending;                  // OK: the last use of 'pending'
```

```cs
var pending = myLock.EnterAsync();
LockGuard b = pending.Result;                 // OK: the last use of 'pending'
```

```cs
var list = new List<LockGuard>();
list.Add(myLock.Enter());
foreach (LockGuard g in list) { }             // OK: the last use of 'list'
```

The test is this strict because a container is a reference. After a span over the list is made, a by-value read of `list[0]` at the last use of `list` would be a second guard, next to the one that the span still reaches. And anything that the list was passed to may have kept it, even a method that only uses it:

```cs
var list = new List<LockGuard>();
list.Add(myLock.Enter());
Fill(list);                              // Fill(List<LockGuard>) only uses the list
LockGuard first = list[0];               // SCA1030: 'Fill' may have kept the list
```

Returning the list after `Fill(list);` is fine: see [Returning a Container](returning-containers.md).

#### Arrays

An element of an array is not read through a member, so no new container hands it out. So a by-value read of an element is a copy of a stored element, even at the last use of an array that is the method's own. Move the element out instead:

```cs
var array = new LockGuard[1];
array[0] = myLock.Enter();
LockGuard first = array[0];                    // SCA1001: a copy of a stored element

LockGuard taken = NonCopyable.Move(ref array[0]);   // OK
```

#### What Is Never a New Container

- **A container parameter** is the caller's, and is never moved from.
- **A container that is read out of a non-copyable value**, by a field, a deconstruction or a pattern, stays stored at the last use of that value, as it is everywhere else.

## What Counts as the Last Use

The last use is a use that takes the value or calls a member on it, when no path uses the variable again before the variable is assigned as a whole. A use is any reference to the variable: a read, a call on it, passing it by reference.

#### Order of Evaluation

The analyzer finds the last use in the order of evaluation, not in the order of the text. With named arguments, the read of `guard` below comes first, so it is not the last use:

```cs
Use(b: guard, a: Touch(ref guard));      // SCA1003: 'Touch' uses 'guard' after the read
```

A setter, an indexer setter or an event accessor runs after the value that it is given has been evaluated. So the setter's use of the variable comes after a read in that value:

```cs
lease.Uses = LeaseOps.Close(lease);           // SCA1003: the setter uses 'lease' after the read
```

#### Loops

Inside a loop, a read of a variable that is declared outside the loop is followed by the same read on the next pass. A backward `goto` is a loop.

#### Exception Paths

An exception is a path too. A use in a `catch`, a `finally` or an exception filter follows every operation of its `try` block:

```cs
LockGuard guard = myLock.Enter();
try { _holder.Take(guard); }           // SCA1003: if Take throws, the catch still uses 'guard'
catch { guard.Dispose(); }

try { Work(); }
catch when (Log(guard)) { }            // SCA1003: if Log gives false, the next catch still uses 'guard'
catch { guard.Dispose(); }
```

- A filter that gives false is followed by the `catch` clauses and filters after it, and after the last of them, by the handlers around the `try` statement. A filter that throws counts as giving false.
- A read inside a filter is never a last use: filters run in a first pass, before the `finally` blocks between the `throw` and the `catch`, and a filter can run again for another exception.
- Where a read gets a warning only because a `catch`, a `finally` or a filter still uses the variable, the message says so.
- The message also says so where the variable is used after a `finally`, including the `finally` that the compiler adds for a `foreach`, a `using` or a `lock`. Code after a `finally` counts as following an exception as well.

> [!TIP]
> One shape is kept clean: a store at the end of the `try` block, after which nothing that can throw runs. See [Commit at the End of a Try Block](commit.md).

## Returned Variables

The analyzer treats a returned variable like every other read. `return guard;` is fine where the variable ends with the method. It gets a warning (SCA1002) where something uses the variable after its value was read: a `finally`, or the end of a `using` scope that holds a reference to it. This holds even with the move on the last use turned off.

```cs
LockGuard guard = myLock.Enter();
try { return guard; }                  // SCA1002: the 'finally' runs after the value was read
finally { guard.Dispose(); }

try { return guard; }                  // OK: the 'finally' resets the variable
finally { guard = default; }
```

## Variables That Are Never Moved From

| Variable | Why |
|---|---|
| A field, an array element, `this`, a `ref`, `in` or `out` parameter | Something else holds the variable |
| A variable that a lambda or a local function captures | The closure still holds it |
| A `using` variable | The end of its scope uses it |
| A parameter of a primary constructor that a member of the type uses | It is a field |
| A parameter of an iterator | It is copied for every enumeration |
| A view, such as a span | It refers to elements stored elsewhere |
| A variable with a reference to it that can outlive the statement | The reference can still reach it (see below) |

#### Primary Constructor Parameters

A parameter of a primary constructor that no member uses is an ordinary parameter. The analysis follows no order between initializers, so the parameter is moved from only if it is used exactly once, in one initializer or in the arguments of the base constructor. A use is a reference that binds to the parameter. Another variable of the same name, such as an `out LockGuard guard` of a member, is not a use.

```cs
class Holder(LockGuard guard) { private LockGuard _guard = guard; }   // OK: used once, in one initializer
```

#### Variables That a Reference Can Outlive

A variable is never moved from if a reference to it, or to a part of it, can outlive the statement. One such use anywhere in the method is enough:

- it is assigned to a `ref` local or a `ref` field, has its address taken, or is given to `__makeref`;
- it is passed by reference to a call or a constructor that returns a reference, a ref struct or a pointer, or that has a `ref` or `out` parameter of such a type;
- it is the receiver of such a call.

An argument for an `in` or `ref readonly` parameter is passed by reference even with no modifier at the call. An inline array that is converted to a span, or sliced, has a reference taken to it even though the code shows none.

## What the Move Does Not Change

- **The variable is not reset,** and no code gets a warning that did not get one before: a read that is not the last use is a copy, as it always was.
- **Where the analyzer cannot decide,** the variable is not moved from.
- **A deconstruction through a `Deconstruct` method** is a use that takes the value: the compiler copies what is deconstructed into a temporary, and a local at its last use is moved into that temporary.

</div>
