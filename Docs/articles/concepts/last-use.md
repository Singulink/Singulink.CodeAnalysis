<div class="article">

# The Last Use of a Variable

A variable that is never used again cannot be used as a second copy. So where a method uses a variable for the last time, the variable is a new
value instead of a stored one, and every rule allows what it allows for a new value: assigning it, passing it by value, putting it in a tuple
or a box, a `using` statement over it, a pattern on it. A project can turn this off: see
[Turning off the move on the last use](../guides/turning-rules-down.md#turning-off-the-move-on-the-last-use).

## An Example

```cs
LockGuard guard = myLock.Enter();
_guard = guard;                          // OK: the last use of 'guard'

var pending = locks.LockAsync(key);      // a ValueTask<KeyLock>
if (!pending.IsCompletedSuccessfully)
    return WaitAsync(pending);           // OK: the last use on this path
using var held = pending.Result;         // OK: the last use on this path

var other = locks.LockAsync(key);
var first = other.Result;                // SCA1030: 'other' is read again below
var second = other.Result;               // OK
```

## Which Variables

- **Which variables:** a local or a by-value parameter whose type is a non-copyable type. That includes a `ValueTask<LockGuard>`, a
  `LockGuard?` and a tuple, a parameter of a lambda or of a setter, and a variable that a pattern, a deconstruction, an `out` argument or a
  `foreach` declares. A part of such a variable, as in `_guard = pair.guard;`, is a new value at the last use of the variable too.
- **A local that holds a container of a reference type,** such as a `Task<LockGuard>` or a `List<LockGuard>`, if it is the method's own by
  [the list of what is allowed](returning-containers.md) for a local that holds containers of containers. That list applies here whatever
  the container holds, without the further uses that a local which holds values directly has there, and with one addition: the local can
  be awaited, and a view of it can be awaited or read where it is made, as in
  `await pending.ConfigureAwait(false)`. Then `await pending`, `pending.Result`, `list[0]` and a `foreach` over the local read from a new
  container at its last use. The test is this strict because a container is a reference: after
  `Span<LockGuard> span = CollectionsMarshal.AsSpan(list);` a by-value read `list[0]` at the last use of `list` would be a second guard
  next to the one that `span[0]` still reaches, and anything that the list was passed to may have kept it. That goes for a method that
  only uses the list as well: after `Fill(list);` the read `LockGuard first = list[0];` is `SCA1030`, where `return list;` is fine.
  An element of an array is not read through a member, so there is no new container that hands it out: `LockGuard first = array[0];`
  is a copy of a stored element (`SCA1001`) at the last use of an array of the method's own too, where `list[0]` is clean. Move it out
  with `NonCopyable.Move(ref array[0])`.
  A container parameter is the caller's, and is never moved from. A container that is read out of a non-copyable value, by a field, a
  deconstruction or a pattern, stays stored at the last use of that value, as it is everywhere else.

## What Counts as the Last Use

- **The last use** is a use that takes the value, or calls a member on it, after which the variable is not used again on any path before it
  is assigned as a whole. A use is any reference to the variable: a read, a call on it, passing it by reference. It is judged in the order
  of evaluation: in `Use(b: guard, a: Touch(ref guard))` the read comes first and is not the last use. A setter, an indexer setter or an
  event accessor runs after the value that is assigned, so in `lease.Uses = Close(lease);` the read in the value is followed by the use by
  the setter. A read in a loop of a variable that is
  declared outside the loop is followed by itself, and a backward `goto` is a loop. An exception is a path too: a use in a `catch`, a
  `finally` or an exception filter follows every operation of its `try` block. A filter that gives false, or that throws, which counts as
  false, is followed by the `catch` clauses and filters after it, and after the last of them by the handlers around the `try` statement.
  A read inside a filter is never a last use: filters run in a first pass, before the `finally` blocks between the `throw` and the
  `catch`, and a filter can run again for another exception.

  ```cs
  LockGuard guard = myLock.Enter();
  try { _holder.Take(guard); }           // SCA1003: if Take throws, the catch still uses 'guard'
  catch { guard.Dispose(); }

  try { Work(); }
  catch when (Log(guard)) { }            // SCA1003: if Log gives false, the next catch still uses 'guard'
  catch { guard.Dispose(); }
  ```

  One shape is kept clean, "acquire, set up, commit, or dispose on failure": the handlers do not follow a read whose value goes into a
  plain store, into a local, a field, an element or a `ref` or `out` parameter and not through a setter or any other call, if nothing
  that can throw runs from there to the end of the `try` block, and the code then leaves the block in the normal way. If the store itself
  fails, the value is still only in the variable. A `finally` always runs, so a use in it follows all the same.

  ```cs
  LockGuard guard = myLock.Enter();
  try { Work(); _held = guard; _state = State.Held; }   // OK: nothing after the store can throw
  catch { guard.Dispose(); throw; }
  ```

  - What cannot throw is a closed list in the analyzer, and whatever is not on it is taken to throw. On it are, in short: a read or a
    plain store of a local or a by-value parameter of the method, a constant, `default`, a tuple literal, the arithmetic, bitwise and
    comparison operators of the language on the simple types (the integers, `bool`, `char`, `float`, `double`, enums) outside a
    `checked` context, the conversions that the language makes with no call, a test with `is` that calls nothing, and the comparison
    of two strings. Not on it, for example: division, `decimal`, `^n` and a range, whose <xref:System.Index> throws for a negative number, an
    explicit conversion, an element of an array, a property, the concatenation of two strings, and any call.
  - Running out of memory is left out of "cannot throw", and so is a thread abort on .NET Framework: boxing, whose only way to fail is
    that memory runs out, is on the list. A result that would be longer than its type allows is not left out, though the runtime
    reports it with the same <xref:System.OutOfMemoryException>: it happens for certain values, and it can be caught. So the concatenation of two
    strings can throw (`_label = _prefix + _suffix;`, `+=` included), unless the compiler folds it to a constant.
  - "Outside a `checked` context" holds for a compound assignment and an increment as a whole. On a type that is narrower than `int`
    the result is converted back, and that conversion is checked where the operator itself cannot overflow: `_slot <<= 1;` on a `byte`
    throws in a `checked` context for a value that no longer fits. The context is the nearest `checked` or `unchecked` block or
    expression around the statement, and with neither it is how the project is compiled (`<CheckForOverflowUnderflow>`).
  - A test with `is` calls nothing where it tests for a class or a struct: `owner is string`. A test for an interface or for a type
    parameter (`owner is IPooled`, also as a type pattern or a declaration pattern) asks an object of a class that implements
    <xref:System.Runtime.InteropServices.IDynamicInterfaceCastable>, by calling a method of that object. So it is on the list only where the static type of what is tested
    is a struct or a sealed class, or where every value of that type passes the test. The sealed class counts only where the type that
    is tested for has no type parameter in it: the compiler knows the answer for `closed is IPooled`, but `closed is T` and
    `closed is IPooled<T>` are answered when the code runs, by the object.
  - In an instance member of a class, a read or a plain store of an instance field of `this` cannot throw, so a state flag or a second
    guard may follow the commit. That is not so for a static field, whose first use can run the initializer of its type, for a field of
    another object, which can be null, for an element of an array, for a property, or for a value that takes a call to compute. Nor is
    it so for a read or a write through a `ref`, `in` or `out` parameter or a `ref` local, and for a field of `this` in a struct, where
    `this` is such a reference: **a reference can be null.** <xref:System.Runtime.CompilerServices.Unsafe.NullRef*?displayProperty=nameWithType> makes one in code that the language calls safe, and
    a read or a write through it fails with a <xref:System.NullReferenceException>. `this` in a struct is such a reference however it is used:
    `this = default;`, `copy = this;`, `this is { State: 1 }`, and a parameter of a primary constructor that the struct keeps, which is
    a field of `this`.
  - A reference that is [known to refer to something](../guides/null-references.md) is the exception, and the
    commit itself is often the proof: after `_held = guard;` in a struct, `this` is known, so `_isHeld = true;` may follow it.
  - An assignment stores when it runs, wherever its target stands. In `node.Owner = _current = guard;` the target `node.Owner` is
    evaluated before the inner store and written after it, and that write is what fails for a null `node`. So the target of every
    assignment after the commit is judged where the assignment stores, also where a conditional in the value made the compiler capture
    the target first, and for every target of a deconstruction.
  - Anything else goes after the `try` statement, and so does the `return`: a `return` with a value that takes more than a plain read,
    a condition, a property, a call.

    ```cs
    LockGuard guard = myLock.Enter();
    try { Work(); _held = guard; }
    catch { guard.Dispose(); throw; }

    Count = _items.Count;                // after the 'try': what can throw no longer reaches the 'catch'
    return Task.CompletedTask;
    ```

  - A block that ends in a `throw` has committed nothing. The exception goes to the very handlers that the rule would skip. Nor has the
    end of a `finally` or of an exception filter, which can pass an exception on:

    ```cs
    try { Work(); }
    catch { _held = guard; throw; }      // SCA1001: the 'finally' still runs after the rethrow
    finally { guard.Dispose(); }
    ```

  - Where a read is reported only because a `catch`, a `finally` or a filter still uses the variable, the message says so. It says so
    too where the variable is used after a `finally`, as the compiler adds one for a `foreach`, a `using` and a `lock`: what follows a
    `finally` counts as following an exception as well.

## Returned Variables and Variables Never Moved From

- **A returned variable is judged like every other read.** `return guard;` is fine where the variable ends with the method, and is
  reported (`SCA1002`) where something uses it after its value was read: a `finally`, or the end of a `using` scope that holds a
  reference to it. That holds with the move on the last use turned off as well.

  ```cs
  LockGuard guard = myLock.Enter();
  try { return guard; }                  // SCA1002: the 'finally' runs after the value was read
  finally { guard.Dispose(); }

  try { return guard; }                  // OK: the 'finally' resets the variable
  finally { guard = default; }
  ```

- **Never moved from:**
  - a field, an array element, `this`, and a `ref`, `in` or `out` parameter: something else holds the variable;
  - a variable that a lambda or a local function captures;
  - a `using` variable, which the end of its scope uses;
  - a parameter of a primary constructor that a member of the type uses, which is a field, and a parameter of an iterator, which is
    copied for every enumeration. A parameter of a primary constructor that no member uses is an ordinary parameter. Initializers have
    no order that the analysis follows, so it is moved from only if it is used exactly once, in one initializer or in the arguments of
    the base constructor: `class Holder(LockGuard guard) { private LockGuard _guard = guard; }` is fine. A use is a reference that
    binds to the parameter: another variable of the same name, such as an `out LockGuard guard` of a member, is none;
  - a view, such as a span;
  - a variable that a reference can outlive the statement of. That is one of which the variable or any part of it is assigned to a `ref`
    local or a `ref` field, has its address taken or is given to `__makeref`, is passed by reference to a call or a constructor that
    returns a reference, a ref struct or a pointer or that has a `ref` or `out` parameter of such a type, or is the receiver of such a
    call. An argument for an `in` or `ref readonly` parameter is passed by reference even with no modifier at the call, and an inline
    array that is converted to a span, or sliced, has a reference taken to it with nothing written at all. One such use anywhere in the
    method is enough.
- **The variable is not reset,** and nothing is reported that was not reported before: a read that is not the last use is a copy as it
  always was. Where the analyzer cannot decide, the variable is not moved from. A deconstruction through a `Deconstruct` method is a use
  that takes the value: the compiler copies what is deconstructed into a temporary, which a local at its last use is moved into.

</div>
