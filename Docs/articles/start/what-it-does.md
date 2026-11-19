<div class="article">

# What the Analyzer Does

Singulink.CodeAnalysis is a set of Roslyn analyzers that warn about every place where C# code copies a struct that must not be copied. This page shows the problem it solves, what a warning looks like and what the analyzer promises.

### The Problem

Some structs own something: a lock guard holds a lock, a handle owns a native handle, a pooled buffer owns an array rented from a pool. C# copies a struct every time it is assigned or passed by value, and nothing in the language stops that. Copy such a struct, and two instances own the same thing:

```cs
var guard = myLock.Enter();     // one guard, which holds the lock
var copy = guard;               // SCA1001: now two guards hold the same lock

guard.Dispose();                // releases the lock
copy.Dispose();                 // releases it again
```

The copy is easy to see here. It is much harder to see when a list, a task or a generic method makes it.

## Marking a Struct

Mark the struct with `[NonCopyable]`, and the build warns wherever it is copied:

```cs
using Singulink.CodeAnalysis;

[NonCopyable]
public struct LockGuard : IDisposable
{
    // ...
}
```

The analyzer follows the value through your code. It warns about the copies, and it lets through the uses that copy nothing. Here `TakeGuard` has two overloads, one that takes the guard by value and one that takes it by `ref`:

```cs
var guard = myLock.Enter();             // OK: a new value
var copy = guard;                       // SCA1001: a copy of the variable
TakeGuard(guard);                       // SCA1003: passed by value
TakeGuard(ref guard);                   // OK: passed by reference
guard.Dispose();                        // OK: called on the variable itself
TakeGuard(NonCopyable.Move(ref guard)); // OK: moved, and 'guard' is left empty

var last = myLock.Enter();
_guard = last;                          // OK: 'last' is not used again, so there is nothing to copy

var guards = new List<LockGuard>();
guards.Add(myLock.Enter());             // OK: a new value is moved in
LockGuard first = guards[0];            // SCA1030: a copy out of the list
LockGuard[] all = guards.ToArray();     // SCA1020: ToArray copies every element
```

It checks only the types that are marked. In a project that references the package directly, it also checks ten framework structs, such as <xref:System.Runtime.InteropServices.GCHandle> (see [Built-in Types](../guides/built-in-types.md)). It has no style or opinion rules. [New and Stored Values](new-and-stored-values.md) explains why some of these lines are fine and others are not.

## What a Warning Looks Like

Every warning has an ID such as `SCA1001`. Its message names the value, says what receives the copy, and says what to do instead. For the `TakeGuard(guard)` line above, in a class named `Worker`, it reads:

```text
warning SCA1003: 'Worker.TakeGuard(LockGuard)' receives a copy of the stored value of non-copyable type 'LockGuard'. Use the value in
place, use a new value, or move the stored value out first with NonCopyable.Move.
```

The ID links to an article on this site, with an example and the ways to fix it. Every ID is a warning by default, and a project can change the severity of any of them. These pages say "warning" whatever the severity is set to.

The IDs come in five groups:

| IDs | What they warn about |
|---|---|
| SCA1001 to SCA1008 | Copies, such as by assignment, by return, by passing on, by boxing and by cloning |
| SCA1010 to SCA1015 | Declarations that copy by themselves, such as an auto-property or an async member of a non-copyable struct |
| SCA1020 to SCA1025 | Generic code, containers that are converted to a type that hides what they hold, and code that was not built with the analyzer |
| SCA1030 to SCA1033 | Containers: reading a value out of a list or a task that still holds it, returning or keeping one that is not yours |
| SCA1040 to SCA1048 | Marks and [symbol list files](../guides/symbol-list.md) that are used wrongly, and code that the analyzer could not check |

Every ID is in [All Diagnostics](../all-diagnostics.md).

## What Is Guaranteed

**What is guaranteed is one instance, not one call of `Dispose`.** In code that the analyzer checks, a value of a marked type is never in two places. Nothing guarantees that a value is disposed, or disposed only once.

So a guard should clear itself in `Dispose`, so that a second call on the same instance does nothing:

```cs
public void Dispose()
{
    _lock?.Exit();
    _lock = null;               // a second Dispose on the same guard does nothing
}
```

A `readonly struct` cannot do that. A struct that your project takes out of the check on purpose, with a `copyable` line in a [symbol list file](../guides/symbol-list.md), is an ordinary struct to the analyzer, and the guarantee does not cover it. Such a file is optional, and most projects never need one.

## What It Cannot See

Ordinary C# code that is built with the analyzers gets a warning wherever it would copy a non-copyable value, even when the copy is made inside the framework or another library. One case can come up in ordinary code: a class of yours that holds a non-copyable value and derives from a base class that clones itself, such as <xref:System.Text.Encoding>. Everything else takes unsafe code, reflection, an API used against its documentation, or something that you vouched for. [What Is Not Covered](../rules/not-covered.md) goes through each.

## Next Steps

Here is where to go from here:

- [Getting Started](getting-started.md) - Install the analyzers and mark your first struct.
- [New and Stored Values](new-and-stored-values.md) - The one idea behind every warning.
- [Why Am I Seeing This?](why-am-i-seeing-this.md) - If the warnings came with a library that you use.

</div>
