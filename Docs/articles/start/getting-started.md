<div class="article">

# Getting Started

This page adds the analyzers to a project, marks a first struct, shows the everyday code that is fine, and walks through a first warning.

### Requirements

The analyzers run inside the C# compiler, so what counts is the SDK that builds the project:

> [!IMPORTANT]
> The analyzers need a C# project built with the .NET 10 SDK or later. They do not load in an older compiler. The target framework of the project does not matter.

## Installing

There are two packages:

| Package | Contents |
|---|---|
| `Singulink.CodeAnalysis` | The attributes, such as <xref:Singulink.CodeAnalysis.NonCopyableAttribute>, and the <xref:Singulink.CodeAnalysis.NonCopyable> helpers. It brings the analyzers with it. |
| `Singulink.CodeAnalysis.Analyzers` | The analyzers only, with no runtime assembly and no dependencies. It adds nothing to your build output. |

Most projects install the first one:

```xml
<PackageReference Include="Singulink.CodeAnalysis" Version="0.7.3" />
```

A project that declares the attributes itself, to have no runtime dependency, installs the second one alone (see [Marking a Struct](../guides/marking-a-struct.md)):

```xml
<PackageReference Include="Singulink.CodeAnalysis.Analyzers" Version="0.7.3" PrivateAssets="all" />
```

The analyzers package is a development dependency, so `dotnet add package` and Visual Studio add it with `PrivateAssets="all"`, which keeps it to your own project. A library that wants the projects that use it checked too passes the analyzers on to them: see [Sharing Your Library](../guides/libraries.md).

In a project that references one of the packages directly, the analyzers also check ten framework structs, such as <xref:System.Threading.SpinLock>: see [Built-in Types](../guides/built-in-types.md).

## Marking a First Struct

Mark the struct with `[NonCopyable]`. Let `Dispose` clear the struct, so that a second call on the same instance does nothing:

```cs
using Singulink.CodeAnalysis;

[NonCopyable]
public struct LockGuard : IDisposable
{
    private MyLock? _lock;

    internal LockGuard(MyLock owner) => _lock = owner;

    public void Dispose()
    {
        _lock?.Exit();
        _lock = null;               // a second Dispose on the same guard does nothing
    }
}
```

From now on, you get a warning for every copy of a `LockGuard`, even where a collection, a task or generic code makes it. `[NonCopyable]` is one of six attributes; the others are for library authors, and [Marking a Struct](../guides/marking-a-struct.md#six-attributes) gives them all. There is nothing else to configure: the framework's collections and tasks are covered without any setup.

## Using a Guard

Most code that uses a guard is fine as it is. A `using` variable is the usual way to hold one:

```cs
using var guard = myLock.Enter();             // OK: disposed at the end of the scope
Console.WriteLine(guard.Id);                  // OK: used in place
```

A method may return a guard: either a new one, or a local that it does not use again:

```cs
LockGuard Open() => myLock.Enter();               // OK: a new guard goes to the caller

LockGuard OpenChecked()
{
    var guard = myLock.Enter();
    Console.WriteLine(guard.Id);
    return guard;                                 // OK: the last use of 'guard'
}
```

A class may hold a guard in a field, and release it in its own `Dispose`:

```cs
sealed class Connection : IDisposable
{
    private LockGuard _guard;                         // OK: a field of a class

    public Connection(MyLock myLock) => _guard = myLock.Enter();   // OK: a new guard

    public void Dispose() => _guard.Dispose();        // OK: disposed in place
}
```

A struct that holds a guard is copied with the guard inside it, so it has to be marked as well:

```cs
struct Holding { LockGuard _guard; }                  // SCA1010: a struct that holds a guard must be marked too

[NonCopyable]
struct MarkedHolding { LockGuard _guard; }            // OK
```

## First Warning

Here a method hands the guard to another method by value, and then uses it again:

```cs
var guard = myLock.Enter();
TakeGuard(guard);               // SCA1003: TakeGuard gets a copy, and 'guard' is used again below
guard.Dispose();
```

The build shows:

```text
warning SCA1003: 'Worker.TakeGuard(LockGuard)' receives a copy of the stored value of non-copyable type 'LockGuard'. Use the value in
place, use a new value, or move the stored value out first with NonCopyable.Move.
```

The message says what receives the copy (`TakeGuard`) and what to do instead. The ID links to [its article](../diagnostics/sca1003.md). There are two fixes, depending on who should own the guard afterwards.

#### Hand the Guard Over

If `TakeGuard` should own the guard, and dispose it, do not use the variable after the call. A local that is not used again is moved, not copied, with a few exceptions that [New and Stored Values](new-and-stored-values.md#last-use-of-a-variable) explains:

```cs
var guard = myLock.Enter();
TakeGuard(guard);               // OK: the last use of 'guard', so the guard moves into TakeGuard
```

#### Lend the Guard

If the caller keeps the guard, let the method take it by reference. Here that method is `UseGuard(ref LockGuard guard)`:

```cs
var guard = myLock.Enter();
UseGuard(ref guard);            // OK: passed by reference
guard.Dispose();
```

[Fixing Common Warnings](fixing-common-warnings.md) has the fixes for the other usual cases, and [Moving Values](../guides/moving-values.md) has more on handing a guard over.

## Next Steps

Here is where to go from here:

- [New and Stored Values](new-and-stored-values.md) - Why one line is a copy and the next is not.
- [Fixing Common Warnings](fixing-common-warnings.md) - The handful of fixes that cover most warnings.
- [All Diagnostics](../all-diagnostics.md) - Every ID, each with an article.

</div>
