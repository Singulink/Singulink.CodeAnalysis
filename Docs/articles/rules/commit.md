<div class="article">

# Commit at the End of a Try Block

A `catch` that disposes a guard is a later use of the guard, so a hand-off inside the `try` block is normally not its [last use](last-use.md), and the `catch` gets a warning for using the guard after it (SCA1016). One shape is kept clean: "acquire, set up, commit, or dispose on failure". This page gives its exact rule.

A hand-off to a member, such as `List<T>.Add`, is no commit. A member that throws may have taken the value, so a `catch` that uses the variable after the call gets the warning. Hand the value over after the `try` statement instead: see [Cleaning Up When Something Throws](../guides/cleanup-when-throwing.md#into-a-collection).

### At a Glance

```cs
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; _state = State.Held; }   // OK: nothing after the store can throw
catch { guard.Dispose(); throw; }
```

The analyzer does not count the handlers as a later use of the variable if all of this holds:

| Condition | Why |
|---|---|
| The value goes into a plain store: a local, a field, an element, a `ref` or `out` parameter, and not a setter or a call | If the store itself fails, the value is still only in the variable |
| Nothing that can throw runs from the store to the end of the `try` block | Otherwise the `catch` could run with the value in two places |
| The code then leaves the block in the normal way | A block that ends in a `throw` hands the exception to the very handlers that the rule would skip |

A `finally` always runs, so a use in a `finally` follows the store all the same.

## What May Follow the Store

What cannot throw is the closed list in [What Counts as Able to Throw](what-can-throw.md). The analyzer assumes that anything not on it can throw. In short, these may follow the store:

- a read or a plain store of a local or a by-value parameter of the method, a constant, `default`;
- the operators of the language on the simple types, outside a `checked` context, except division and a few more;
- in an instance member of a class, a read or a plain store of an instance field of `this`;
- a read or a plain store through a reference that is [known to refer to something](null-references.md).

#### Fields of This, in a Class

In an instance member of a class, a read or a plain store of an instance field of `this` cannot throw, so a state flag or a second guard may follow the commit. Other fields and references can throw, as [the closed list](what-can-throw.md#reads-and-stores) gives. A static field can run the initializer of its type, another object whose field is used can be null, the index of an element can be out of range, and a property is a call. A read or a write through a `ref`, `in` or `out` parameter, a `ref` local or `this` in a struct can meet a null reference.

```cs
// In a class:
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; s_count++; }   // the static field can run its type's initializer...
catch { guard.Dispose(); throw; }           // SCA1016: ...so the catch can still run after the store
```

#### This in a Struct

`this` in a struct is a reference however it is used, so after the commit any use of it counts as able to fail: see [Reads and Stores](what-can-throw.md#reads-and-stores). The exception is a reference that is known to refer to something, and the commit itself is often the proof. After `_held = guard;` in a struct, `this` is known to refer to something, so `_isHeld = true;` may follow.

```cs
public void Acquire()                       // in a struct
{
    LockGuard guard = _lock.Enter();
    try
    {
        Prepare();
        _held = guard;                      // the commit; also a store through 'this'
        _isHeld = true;                     // OK: 'this' is known to refer to something
    }
    catch { guard.Dispose(); throw; }
}
```

#### Where an Assignment Stores

An assignment stores its value when the assignment runs, wherever its target appears in the text:

```cs
node.Owner = _current = guard;      // 'node.Owner' is evaluated first, but written last, and that write fails for a null 'node'
```

So the analyzer judges the target of every assignment after the commit at the point where the assignment stores. That holds even where a conditional in the value made the compiler capture the target first, and it holds for every target of a deconstruction.

## What Goes After the Try Statement

Anything else goes after the `try` statement. So does a `return` whose value takes more than a plain read, such as a condition, a property or a call.

```cs
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; }
catch { guard.Dispose(); throw; }

Count = _items.Count;                // after the 'try': what can throw no longer reaches the 'catch'
return Task.CompletedTask;
```

## Blocks That Commit Nothing

A block that ends in a `throw` has committed nothing: the exception goes to the very handlers that the rule would skip. Nor does the end of a `finally` or of an exception filter commit anything, since either can pass an exception on:

```cs
try { Work(); }
catch { _held = guard; throw; }      // the 'finally' still runs after the rethrow...
finally { guard.Dispose(); }         // SCA1016: ...and uses 'guard' after the store
```

Where the first thing after the store that can throw is a read or a write through a reference that is not known to refer to something, the message of SCA1016 at the handler names that reference and says how to check it.

</div>
