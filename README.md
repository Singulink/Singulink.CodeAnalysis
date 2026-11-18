# Singulink.CodeAnalysis

[![Chat on Discord](https://img.shields.io/discord/906246067773923490)](https://discord.gg/EkQhJFsBu6)
[![View nuget packages](https://img.shields.io/nuget/v/Singulink.CodeAnalysis.svg)](https://www.nuget.org/packages/Singulink.CodeAnalysis/)
[![Build and Test](https://github.com/Singulink/Singulink.CodeAnalysis/workflows/build%20and%20test/badge.svg)](https://github.com/Singulink/Singulink.CodeAnalysis/actions?query=workflow%3A%22build+and+test%22)

**Singulink.CodeAnalysis** is a set of Roslyn analyzers that find every place where code copies a struct that must never be copied. A lock
guard, a handle or a pooled buffer is a struct where each instance owns something that only one instance may own: copy it, and two instances
release the same lock or free the same handle. Mark such a struct with `[NonCopyable]`, and the build warns wherever it is copied, including
the copies that are easy to miss: a read out of a list or a task, a value passed through generic code, and the copies that the compiler makes
by itself for patterns, iterators and read-only references.

The analyzers check only what is marked, and there are no style or opinion rules. All the rules so far, the
[SCA1000s](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/getting-started.html#the-diagnostics), are about non-copyable structs.

```cs
using Singulink.CodeAnalysis;

[NonCopyable]
public struct LockGuard : IDisposable
{
    // ...
}

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

**What is guaranteed is one instance, not one call of `Dispose`.** A value of a marked type is never in two places in code that the
analyzer checks. A type that the project takes out with a [copyable line](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/symbol-list.html) is an ordinary struct for it, and outside
that guarantee. Nothing says that it is disposed, or disposed once: a guard should clear itself in `Dispose`, so that a second call on the
same instance does nothing. A `readonly struct` guard cannot do that.

### About Singulink

We are a small team of engineers and designers dedicated to building beautiful, functional and well-engineered software solutions. We offer very competitive rates as well as fixed-price contracts and welcome inquiries to discuss any custom development / project support needs you may have.

This package is part of our **Singulink Libraries** collection. Visit https://github.com/Singulink to see our full list of publicly available libraries and other open-source projects.

## Why Am I Seeing This?

You may not have added these analyzers yourself. A library whose public API has structs that must not be copied, such as lock guards or
handles, can bring the analyzers with it, so that the projects which use the library are warned when they copy those structs. The
analyzers then check the types that the library marked, and nothing else: with nothing marked in your own code, a finding appears only
where your code copies one of the library's structs, or a collection or a task that holds one. The ten framework structs that the
analyzers know as non-copyable stay off in such a project.

Each finding has an ID of the form `SCA1001`, and its message names the value, says what takes the copy and what to do instead. The ID
links to the article of the finding on the [documentation site](https://www.singulink.com/Docs/Singulink.CodeAnalysis/index.html), with an example that is reported and the forms
that are not. Most findings are fixed in one of a few ways, listed under [Common Fixes](#common-fixes) below.

## New Values and Stored Values

Every rule comes down to one distinction:

- A **new value** is one that nobody else holds: `new()`, `default`, the result of a call or of an operator, the result of `NonCopyable.Move`, and
  a local or a by-value parameter [where it is used for the last time](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/concepts/last-use.html). It can go anywhere once: into a variable, a
  field, a parameter, a tuple, an array, a collection, a hole of an interpolated string.
- A **stored value** is anything read from a local, a parameter, a field, an array element or a reference. Reading it by value makes a copy, and
  the copy is what is reported. It can be used in place (`guard.Dispose()`), passed by `ref`, `in` or `ref readonly`, and referred to with a
  `ref` local or a `ref` return.

See [New Values and Stored Values](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/concepts/new-and-stored-values.html) for the rest of the distinction: the result of an
assignment, the last use of a variable, and what else is stored.

## Common Fixes

Most findings are fixed in one of a few ways.

**An element of a list:** read it through a span, which gives a reference to the element.

```cs
LockGuard first = guards[0];                              // SCA1030: a copy out of the list
guards[0].Dispose();                                      // SCA1030: disposes a copy

CollectionsMarshal.AsSpan(guards)[0].Dispose();           // OK: in place
ref LockGuard held = ref CollectionsMarshal.AsSpan(guards)[0];
```

**A loop over the elements:** declare a `ref` variable over a span of them.

```cs
foreach (var guard in guards) guard.Dispose();            // SCA1030: each 'guard' is a copy

foreach (ref var guard in CollectionsMarshal.AsSpan(guards)) guard.Dispose();   // OK
foreach (ref var guard in array.AsSpan()) guard.Dispose();                      // OK
```

**A value in a dictionary:** take it out with the member that removes it. To release every value, take a snapshot of the keys first.

```cs
LockGuard guard = held[key];                              // SCA1030: a copy, and the dictionary still has the guard

if (held.Remove(key, out var taken)) taken.Dispose();     // OK: taken out

int[] keys = [.. held.Keys];                              // OK: the keys only
foreach (int k in keys) { if (held.Remove(k, out var each)) each.Dispose(); }
```

**A field that is handed on:** move it out, which leaves the field empty.

```cs
return _guard;                                            // SCA1002
return NonCopyable.Move(ref _guard);                      // OK

Take(_guard);                                             // SCA1003
Take(NonCopyable.Move(ref _guard));                       // OK
```

**A variable that is used after it was handed on:** read what you need first. A variable is a new value where it is used for the last time,
so the hand-over has to be that last use.

```cs
_held = guard;                                            // SCA1001: 'guard' is read again below
Log(guard.Id);

int id = guard.Id;
_held = guard;                                            // OK: the last use
Log(id);
```

**A callback that works on a guard:** `Func<>` and `Action<>` take their arguments by value, and an `async` lambda cannot have a `ref`
parameter. Declare a delegate with a `ref` parameter. For `async` work over a guard, keep the guard in an object and pass that.

```cs
public delegate TResult GuardFunc<TResult>(ref LockGuard guard);

public TResult With<TResult>(GuardFunc<TResult> body)
{
    var guard = Enter();
    try { return body(ref guard); }
    finally { guard.Dispose(); }
}

int count = locks.With((ref LockGuard guard) => guard.Count);
```

**A constructor that validates and then takes:** take the value through a `[MovedFrom] ref` parameter, so that the caller still has it when
the constructor throws. Read it and clear it in two statements with nothing between them.

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

**A task that a waiter completes later:** make the `TaskCompletionSource` in the method that returns its task, and read `Task` once, there.
See [What a listed member hands out](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/concepts/listed-members.html).

```cs
var source = new TaskCompletionSource<LockGuard>();
_waiters.Enqueue(source);
return source.Task;                                       // OK: made here, read once
```

Three shapes of correct code stay reported, with the clean form or the suppression for each: see
[Correct Code That Stays Reported](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/common-fixes.html#correct-code-that-stays-reported).

## Turning Things Down

**One type that you copy on purpose:** a struct that the built-in list or a library makes non-copyable is taken out with a `copyable`
line in a file named `NonCopyableSymbols.txt` in the folder of the project, which the analyzers read with nothing more written:

```
T:System.Runtime.InteropServices.GCHandle;copyable
```

**A rule:** every rule is a warning, and is turned down like any other analyzer rule, in an `.editorconfig` file:

```ini
[*.cs]
dotnet_diagnostic.SCA1023.severity = none
```

**One finding that is correct code:** suppress it where it stands, with a comment that says why it is right. See
[Turning Rules Down](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/turning-rules-down.html) and [The Symbol List](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/symbol-list.html).

## Marking a Struct of Your Own

Reference `Singulink.CodeAnalysis` and mark the struct with `[NonCopyable]`, as in the example above. The analyzers recognize the
attribute by its full name, so a project that wants no runtime dependency can declare the attributes itself, in the namespace
`Singulink.CodeAnalysis`, and reference only `Singulink.CodeAnalysis.Analyzers`. See
[Marking a Struct](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/marking-a-struct.html), and [Libraries](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/libraries.html) for passing
the analyzers on to the projects that use your library.

## Installation

There are two packages:

| Package | Contents |
|---|---|
| `Singulink.CodeAnalysis` | The attributes, such as `Singulink.CodeAnalysis.NonCopyableAttribute`, the `NonCopyable.Move` helper, and the checks `NonCopyable.ThrowIfNullRef` and `NonCopyable.ThrowIfNullRefArgument`, in assemblies for .NET Standard 2.0 and .NET 10. It depends on `Singulink.CodeAnalysis.Analyzers`, so referencing it brings the analyzers too. |
| `Singulink.CodeAnalysis.Analyzers` | The analyzers only. It has no runtime assembly and no dependencies, so it adds nothing to your build output. |

Install **`Singulink.CodeAnalysis`** if you want to use the attributes from the package:

```xml
<PackageReference Include="Singulink.CodeAnalysis" Version="0.5.0" />
```

Install **`Singulink.CodeAnalysis.Analyzers`** alone if you declare the attributes yourself (see [Marking a Struct of Your Own](#marking-a-struct-of-your-own) above) and want
no runtime dependency:

```xml
<PackageReference Include="Singulink.CodeAnalysis.Analyzers" Version="0.5.0" PrivateAssets="all" />
```

A project that references one of the two packages itself also has ten framework structs checked as non-copyable types, such as `GCHandle` and
`SpinLock`. A project that only gets the analyzers through a library does not. See
[Non-copyable types](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/concepts/non-copyable-types.html) and
[Leaving out the built-in non-copyable types](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/symbol-list.html#the-built-in-non-copyable-types).

The analyzers package is marked as a development dependency, so `dotnet add package` and Visual Studio add it with `PrivateAssets="all"`, which keeps it
to your own project. To let it reach the projects that use your library, see [Libraries](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/libraries.html).

**Requirements**: C# projects built with the .NET 10 SDK or later, that is, a compiler of version 5.0 or later. The analyzer does not load in an
older compiler. The target framework of the project does not matter. The analyzers are tested with the compilers of the .NET 10 and .NET 11
SDKs. With a newer SDK, new syntax that the analyzers do not know is reported as a use that is not known to be safe, and a copy that a newer
compiler makes inside syntax that they already know is covered once that SDK has been tested.

## Further Reading

Here are some links to get you started:

- [Getting Started](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/getting-started.html) - A walkthrough, and the entry point to the guides on every part of
  using the analyzers.
- [The Diagnostics](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/guides/getting-started.html#the-diagnostics) - Every ID, each with an article on what the finding
  means and how to fix it.
- [New Values and Stored Values](https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/concepts/new-and-stored-values.html) - The concepts behind the rules.
- [API Documentation](https://www.singulink.com/Docs/Singulink.CodeAnalysis/api/Singulink.CodeAnalysis.html) - The attributes and the helpers.
- [Chat on Discord](https://discord.gg/EkQhJFsBu6) - Have questions or want to discuss the library? This is the place for all Singulink
  project discussions.
- [Third-party notices](https://github.com/Singulink/Singulink.CodeAnalysis/blob/main/THIRD-PARTY-NOTICES.md) - The non-copyable analyzer
  is derived from Roslyn's `DoNotCopyValue` analyzer, and some of its tests use the test sources of hamarb123.Analyzers, both under the MIT
  license.
