<div class="article">

# Last Use of a Variable

A variable that is not used again leaves no second copy behind. So where a method uses a variable for the last time, the variable is a new value instead of a stored one. There, every rule allows what it allows for a new value: you can assign it, pass it by value, put it in a tuple or a box, use it in a `using` statement, or match a pattern on it.

### At a Glance

- **Which variables:** a local or a by-value parameter of a non-copyable type, and a local that holds a container which is the method's own.
- **The last use:** a use after which no path, an exception path included, uses the variable again before the variable is assigned as a whole.
- **A read by value that is not the last use** is a copy, reported at the first use after it (SCA1016).
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
var first = other.Result;                // a copy, since 'other' is read again below
var second = other.Result;               // SCA1016: 'other' is used after its value was handed over
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

A local that holds a container of a reference type, such as a `Task<LockGuard>` or a `List<LockGuard>`, has to meet a stricter test. It is a new container at its last use only if it passes the **stricter** of the two tests of [a local that the method owns](owned-locals.md): the [first list](owned-locals.md#what-any-local-may-do), whatever the local holds. The extra uses that a local of guards may have and still be returned do not count here.

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
Use(b: guard, a: Touch(ref guard));      // SCA1016 at the second 'guard': 'Touch' uses it after the read
```

A setter, an indexer setter or an event accessor runs after the value that it is given has been evaluated. So the setter's use of the variable comes after a read in that value:

```cs
lease.Uses = LeaseOps.Close(lease);           // SCA1016 at the first 'lease': the setter uses it after the read
```

A compound assignment, such as `+=` or `??=`, reads its target before its right side runs, and writes it after. So the read of the target counts before the right side:

```cs
LeaseOps.Take(lease);
lease.Uses += LeaseOps.Reset(out lease);      // SCA1016 at the first 'lease': the getter reads it before Reset assigns it
```

#### Loops

Inside a loop, a read of a variable that is declared outside the loop is followed by the same read on the next pass. A backward `goto` is a loop.

#### Exception Paths

An exception is a path too. A use in a `catch`, a `finally` or an exception filter follows every operation of its `try` block:

```cs
LockGuard guard = myLock.Enter();
try { _holder.Take(guard); }           // if Take throws...
catch { guard.Dispose(); }             // SCA1016: ...the catch still uses 'guard'
```

```cs
LockGuard guard = myLock.Enter();
try { Work(); }
catch when (Log(guard)) { }            // SCA1003: if Log gives false, the next catch still uses 'guard'
catch { guard.Dispose(); }
```

- A filter that gives false is followed by the `catch` clauses and filters after it, and after the last of them, by the handlers around the `try` statement. A filter that throws counts as giving false.
- A read inside a filter is never a last use: filters run in a first pass, before the `finally` blocks between the `throw` and the `catch`, and a filter can run again for another exception. Such a read is reported where it is, and the message says why.
- Where the later use is in a `catch`, a `finally` or a filter, the message of SCA1016 says that the handler can still run.

A member that throws may have taken the value, so a `catch` that uses the variable after a hand-off is reported. That holds for every member, also one that takes the value only where it returns true:

```cs
Lease lease = Pools.Rent();
try { _pending.Add(lease); }
catch { lease.Dispose(); throw; }             // SCA1016: Add may have taken the lease before it threw
```

> [!TIP]
> One shape is kept clean: a store at the end of the `try` block, after which nothing that can throw runs. See [Commit at the End of a Try Block](commit.md). For a hand-off to a member, do the work that can fail inside the `try`, and hand the value over after it: see [Cleaning Up When Something Throws](../guides/cleanup-when-throwing.md#into-a-collection).

## Use After a Hand-off

A read by value that is not the last use of the variable copies the value. The analyzer reports that copy at the first use after the read on each path, as SCA1016, with the read as an additional location: the use is where the code goes wrong.

- **A path ends** where the variable is assigned as a whole. An exception that can leave a block leads to its handlers.
- **A use that two reads reach** is reported once. A use that is itself a hand-off is reported, and is the hand-off for the uses after it.
- **The branch where a member did not take the value** has no use after the hand-off. A member whose symbol list entry has the [`borrowswhen`](symbol-list-format.md#borrowswhen) marker takes the value only on one branch of the condition that its result goes straight into, so the variable is still the caller's on the other branch.

The copy stays at the read, with the ID of the copy (SCA1001, SCA1002 or SCA1003), where there is no use of its own to report:

| Read | Why |
|---|---|
| A read that is its own later use, the next time round a loop | The use after it is the read itself |
| A read whose later use has no place of its own in the source | There is nothing else to point at |
| A read whose later uses an exception reaches only through the end of a `finally` | The same |
| A read of a variable that is [never moved from](#variables-that-are-never-moved-from), or that a reference can outlive | It is a copy whatever follows |
| A read in an exception filter | It is never a last use |
| Two uses in one operation, such as `guard.With(ref guard)` | The call uses both |

A read out of a container in a variable, such as a task or a list, is judged by whether the method owns the container (see [Locals That Hold a Container](#locals-that-hold-a-container)), and is reported at the read (SCA1030).

## Returned Variables

The analyzer treats a returned variable like every other read. `return guard;` is fine where the variable ends with the method. Where a `finally` uses the variable after the `return` has read its value, the `return` handed the value over, and the use in the `finally` gets SCA1016. Where the end of a `using` scope that holds a reference to it uses the variable, the `return` gets SCA1002.

```cs
LockGuard guard = myLock.Enter();
try { return guard; }                  // hands the guard over...
finally { guard.Dispose(); }           // SCA1016: ...and the 'finally' still uses 'guard'

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

- **The variable is not reset.** A read that is not the last use is a copy, and a use after a move by hand finds the default value.
- **Where the analyzer cannot decide,** the variable is not moved from.
- **A deconstruction through a `Deconstruct` method** is a use that takes the value: the compiler copies what is deconstructed into a temporary, and a local at its last use is moved into that temporary.

</div>
