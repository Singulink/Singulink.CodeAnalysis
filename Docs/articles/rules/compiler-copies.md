<div class="article">

# Copies the Compiler Makes

Some constructs of the language copy a value although the code shows no copy. Most of them look like one use of a value but are two, and those get a warning whatever the value is, even a new one. The others copy a stored value once, and get a warning like any other copy. This page lists them.

### At a Glance

| Construct | What the compiler does | Warning |
|---|---|---|
| The clauses of a `switch`, after a clause with a `when` | A later clause binds the value again | SCA1001 |
| An awaiter of a non-copyable type | The state machine copies it | SCA1015 |
| Combined delegates | Every target gets a copy of a by-value argument | SCA1003 |
| User-defined `&&` and `\|\|` | The left operand goes to `operator false` and then on | SCA1003 |
| An argument that an interpolated-string handler also receives | The method and the handler both get it | SCA1003 |
| An indexer with a compound assignment or an increment | The getter and the setter both get the index | SCA1003 |
| A by-value receiver of an extension block that two members get | The getter and the setter both get it | SCA1003 |
| A compound assignment operator that is an instance member | It is a call with the right side as the argument | SCA1003 for a stored value |
| A spread | It reads every element by value | SCA1030 |
| A query expression | It calls its method on a temporary | SCA1006 |
| A member that the compiler calls on the receiver of an extension | A by-value receiver is copied | SCA1003 |

## Clauses of a Switch

One value goes to one variable across the clauses of a `switch`, as within a pattern. A clause with a `when` runs code with its variable and then lets a later clause bind the value again, so a variable of a later clause that overlaps it is a copy (SCA1001). A clause without a `when` is harmless.

```cs
switch (myLock.Enter())
{
    case var registered when TryRegister(registered): break;   // takes the guard, and releases it if it fails
    case var rejected: rejected.Dispose(); break;              // SCA1001: a second variable for the same guard
}
```

## Awaiters

When an `await` suspends, the compiler copies the awaiter into its state machine, then calls `OnCompleted` on the original and `GetResult` on the copy. So an awaiter of a non-copyable type gets a warning (SCA1015) at the `await`:

```cs
[NonCopyable]
public struct GuardAwaiter : INotifyCompletion { /* ... */ }

public sealed class Waitable { public GuardAwaiter GetAwaiter() => default; }

await waitable;                               // SCA1015: the state machine copies the awaiter
```

Make the awaiter a class, or a copyable struct that refers to the state. The awaiters of the framework hold a reference to their task and are fine.

## Combined Delegates

When a delegate has several targets, each target gets its own copy of a by-value argument. `+` and `+=` on a delegate, and `+=` on a field-like event, get a warning (SCA1003) where the delegate type has a by-value parameter of a non-copyable type.

```cs
// delegate void GuardHandler(LockGuard guard);
GuardHandler both = Close;
both += Log;                                  // SCA1003: each target gets a copy of the guard
```

## User-defined And and Or

A user-defined `&&` evaluates as `operator false(x) ? x : operator &(x, y)`, so the left operand is passed by value twice:

```cs
// Ticket is non-copyable, with operator true, operator false and operator &
if (MakeTicket() && MakeTicket()) { }         // SCA1003: the first ticket goes to 'operator false' and again to 'operator &'
```

The same goes for `||` with `operator true`.

## Interpolated-string Handlers

With `[InterpolatedStringHandlerArgument("guard")]` the compiler passes the argument, or the receiver, to the method and again to the constructor of the handler (SCA1003). Take it by `in` or `ref` in both:

```cs
static void Log(LockGuard guard, [InterpolatedStringHandlerArgument("guard")] GuardLogHandler handler) { }
static void LogIn(in LockGuard guard, [InterpolatedStringHandlerArgument("guard")] InGuardLogHandler handler) { }

Log(myLock.Enter(), $"held {1}");             // SCA1003: the method and the handler each get a copy
LogIn(in guard, $"held {1}");                 // OK: both take it by 'in'
```

## Indexers and Extension Receivers

`counts[guard] += 1` and `counts[guard]++` pass the index to the getter and to the setter (SCA1003).

A by-value receiver of an extension block is passed to two members in the same way:

- `g.Uses += 1`, `g.Uses++` and `g.Uses ??= 1` on a property of `extension(LockGuard g)` pass `g` to the getter and to the setter, and so does an indexer of such a block (C# 15);
- an index or a range through the length, the indexer and `Slice` of such a block (`g[^1]`, `g[1..]`) passes `g` to each of them.

Each of them also takes a stored value by value, like any other member with a by-value receiver. Take the receiver by `ref`.

```cs
counts[guard] += 1;                           // SCA1003: the getter and the setter both get 'guard'
g.Uses += 1;                                  // SCA1003: the getter and the setter both get 'g'
```

## Compound Assignment Operators

A compound assignment operator that is an instance member (`public void operator +=(T item)`) is a call of that member on its target, with the right side as the argument:

```cs
pile += myLock.Enter();                       // OK: a new value is moved in
pile += _held;                                // SCA1003: a copy of the stored guard
```

## Spreads

`[.. span]` reads every element by value, like a `foreach` with a by-value variable, even over a view that a call has just returned (SCA1030):

```cs
LockGuard[] copies = [.. CollectionsMarshal.AsSpan(_list)];   // SCA1030: each element is read by value
```

A spread also calls the members that a `foreach` calls, without the code naming them. The analyzer checks `[.. bag]` for `GetEnumerator`, `MoveNext`, `Current` and `Dispose`, just as it checks a `foreach` over `bag`. So a member marked `[RequiresCopyable]`, or a `GetEnumerator` that is not on the [symbol list](symbol-list-format.md), gets a warning there too (SCA1021, SCA1020), and so does one that runs on a copy (SCA1006).

## Query Expressions

A query expression evaluates the source of its `from` clause into a temporary and calls `Select` on that temporary. A call of `Select` written out in the code runs in place instead. A method of a non-copyable source that is not `readonly` runs on a copy unless the source is a new value (SCA1006):

```cs
// _queue is a non-copyable struct with a Select method that is not readonly
var q = from item in _queue select item;      // SCA1006: 'Select' runs on a copy of '_queue'
int r = _queue.Select(item => item);          // OK: in place
```

## Members That the Compiler Calls on the Receiver of an Extension

The compiler calls `GetEnumerator` for a `foreach`, `GetAwaiter` for an `await`, and `Deconstruct` for a deconstruction or a positional pattern, with no argument written in the code. Where such a member is an extension that takes its receiver by value, in a block or in the classic form, a stored value is copied for the call (SCA1003):

```cs
public static class GuardEnumeration
{
    public static IdEnumerator GetEnumerator(this LockGuard guard) => new();
}

foreach (int id in _guard) { }                // SCA1003: 'GetEnumerator' receives a copy of the field
```

</div>
