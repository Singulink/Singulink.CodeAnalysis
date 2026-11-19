<div class="article">

# Cleaning Up When Something Throws

A method that acquires a guard often has to release it if the setup that follows fails. This page shows the shape that the analyzer keeps clean: acquire, set up, commit, or dispose on failure.

### At a Glance

```cs
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; _state = State.Held; }   // OK: nothing after the store can throw
catch { guard.Dispose(); throw; }
```

- **Store the guard last** in the `try` block, with a plain assignment.
- **Run nothing that can throw after the store.** In a class, setting a field of the object is fine.
- **Let the block end normally,** not with a `throw`.

## Why the Catch Matters

A `catch` that disposes the guard is a later use of the guard. So a handover inside the `try` is not the guard's last use, and is a copy:

```cs
LockGuard guard = myLock.Enter();
try { _holder.Take(guard); }           // SCA1003: if Take throws, the catch still uses 'guard'
catch { guard.Dispose(); }
```

The message says so: "'guard' is not used again after this, but a 'catch', a 'finally' or an exception filter that uses it can still run."

## Commit

The one shape that is kept clean is a commit at the end of the `try` block. The guard is stored with a plain assignment, and nothing that can throw runs from there to the end of the block. The store can go into a field, a local, an element, or a `ref` or `out` parameter. If the store itself fails, the guard is still only in the variable, and the `catch` disposes the one guard.

#### What May Follow the Store

| After the store | Allowed? |
|---|---|
| A read or a write of a local, a constant, and `+`, `-`, `*` or a comparison on numbers, outside a `checked` context | Yes |
| Division, remainder, `decimal` arithmetic, the concatenation of two strings | No |
| A field of `this`, in a class | Yes |
| A static field, a property, a call, an element of an array | No: move it after the `try` statement |
| A field of `this`, in a struct | Only once `this` is known to refer to something, which the store itself often proves |

Anything that can throw goes after the `try` statement, and so does a `return` with more than a plain read:

```cs
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; }
catch { guard.Dispose(); throw; }

Count = _items.Count;                // after the 'try': what can throw no longer reaches the 'catch'
return Task.CompletedTask;
```

The exact list of what cannot throw is long and strict: see [What Counts as Able to Throw](../rules/what-can-throw.md).

#### In a Struct

In a struct, a field of `this` is reached through a reference that can be null. The store of the guard into a field of `this` proves that `this` refers to something, so a flag may follow it:

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

## A Finally Always Runs

A `finally` runs after the commit as well, so a use in a `finally` always comes after the store. A block that ends in a `throw` commits nothing either:

```cs
try { Work(); }
catch { _held = guard; throw; }      // SCA1001: the 'finally' still runs after the rethrow
finally { guard.Dispose(); }
```

When the `finally` should release the guard only if it was not handed over, move the guard out in the `try` instead, which clears the variable:

```cs
LockGuard guard = myLock.Enter();
try { Work(); _held = NonCopyable.Move(ref guard); }   // OK: 'guard' is left empty
finally { guard.Dispose(); }                           // disposes nothing if the guard was handed over
```

## Constructors That Validate and Then Take

A constructor that may throw before it takes the guard cannot take it by value: the caller's `catch` would still use the guard. Take it through a `[MovedFrom] ref` parameter, so that the caller still has it when the constructor throws:

```cs
try { return new UnitOfWork(scope, user); }               // SCA1003: the 'catch' can still run, so this is not the last use
catch { scope.Dispose(); throw; }

public UnitOfWork([MovedFrom] ref TransactionScope scope, string user)
{
    ArgumentException.ThrowIfNullOrEmpty(user);           // throws before anything is taken
    _scope = scope;
    scope = default;
}

try { return new UnitOfWork(ref scope, user); }           // OK
catch { scope.Dispose(); throw; }
```

See [Writing a Move Method](writing-a-move.md) for the rules of such a parameter.

The exact rules of the commit: [Commit at the End of a Try Block](../rules/commit.md).

</div>
