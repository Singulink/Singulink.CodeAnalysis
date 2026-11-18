<div class="article">

# Getting Started

This guide adds the analyzers to a project, marks a first struct and shows how the findings are named. The other guides take each part further, and the concepts explain the rules behind the findings.

### Packages

There are two packages:

| Package | Contents |
|---|---|
| `Singulink.CodeAnalysis` | The attributes, such as <xref:Singulink.CodeAnalysis.NonCopyableAttribute>, the <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> helper, and the checks <xref:Singulink.CodeAnalysis.NonCopyable.ThrowIfNullRef*?displayProperty=nameWithType> and <xref:Singulink.CodeAnalysis.NonCopyable.ThrowIfNullRefArgument*?displayProperty=nameWithType>, in assemblies for .NET Standard 2.0 and .NET 10. It depends on `Singulink.CodeAnalysis.Analyzers`, so referencing it brings the analyzers too. |
| `Singulink.CodeAnalysis.Analyzers` | The analyzers only. It has no runtime assembly and no dependencies, so it adds nothing to your build output. |

Install **`Singulink.CodeAnalysis`** if you want to use the attributes from the package:

```xml
<PackageReference Include="Singulink.CodeAnalysis" Version="0.5.0" />
```

Install **`Singulink.CodeAnalysis.Analyzers`** alone if you declare the attributes yourself (see [Marking a Struct](marking-a-struct.md)) and want no runtime
dependency:

```xml
<PackageReference Include="Singulink.CodeAnalysis.Analyzers" Version="0.5.0" PrivateAssets="all" />
```

The analyzers package is marked as a development dependency, so `dotnet add package` and Visual Studio add it with `PrivateAssets="all"`, which keeps it
to your own project. To let it reach the projects that use your library, see [Libraries](libraries.md).

**Requirements**: C# projects built with the .NET 10 SDK or later, that is, a compiler of version 5.0 or later. The analyzer does not load in an
older compiler. The target framework of the project does not matter. The analyzers are tested with the compilers of the .NET 10 and .NET 11
SDKs. With a newer SDK, new syntax that the analyzers do not know is reported as a use that is not known to be safe, and a copy that a newer
compiler makes inside syntax that they already know is covered once that SDK has been tested.

## Marking a First Struct

The first set of rules, the [SCA1000s](#the-diagnostics), reports code that copies a struct which must not be copied, such as a lock guard, a handle, or any
other struct where each instance owns something that only one copy may own. Mark the struct with `[NonCopyable]`, and copying it becomes a warning,
also where the copy is made by a collection, a task or other generic code.

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
analyzer checks. A type that the project takes out with a [copyable line](symbol-list.md) is an ordinary struct for it, and outside
that guarantee. Nothing says that it is disposed, or disposed once: a guard should clear itself in `Dispose`, so that a second call on the
same instance does nothing. A `readonly struct` guard cannot do that.

Most findings are fixed in one of a few ways: see [Common Fixes](common-fixes.md). Why a value is fine in one place and reported in another is the subject of [New Values and Stored Values](../concepts/new-and-stored-values.md).

## The Diagnostics

Diagnostic IDs are `SCA` followed by four digits. Each kind of rule has its own thousand: the `SCA1000`s are the non-copyable rules. All are warnings
in the category Reliability.

| ID | Finding |
|---|---|
| [SCA1001](../diagnostics/sca1001.md) | Copy by assignment from a variable, field or reference |
| [SCA1002](../diagnostics/sca1002.md) | Copy by returning a field or reference by value |
| [SCA1003](../diagnostics/sca1003.md) | Passed by value, captured, or another use that is not known to be safe |
| [SCA1004](../diagnostics/sca1004.md) | A stored non-copyable value boxed or converted to `object` or an interface |
| [SCA1005](../diagnostics/sca1005.md) | <xref:System.Object.MemberwiseClone> of an object or a struct that holds a non-copyable value |
| [SCA1006](../diagnostics/sca1006.md) | Member that the compiler runs on a copy, because it is used through a read-only reference |
| [SCA1010](../diagnostics/sca1010.md) | Field of a non-copyable type in a copyable struct or a record |
| [SCA1011](../diagnostics/sca1011.md) | Auto-property of a non-copyable type |
| [SCA1012](../diagnostics/sca1012.md) | By-value parameter of a non-copyable type on an iterator that returns a sequence |
| [SCA1013](../diagnostics/sca1013.md) | Struct of a non-copyable type that leaves an interface member to its default implementation |
| [SCA1014](../diagnostics/sca1014.md) | `async` or iterator instance member of a non-copyable struct that is not `readonly` |
| [SCA1015](../diagnostics/sca1015.md) | `await` whose awaiter is of a non-copyable type |
| [SCA1020](../diagnostics/sca1020.md) | Member of an unmarked generic used with a non-copyable type, not on the list |
| [SCA1021](../diagnostics/sca1021.md) | Member that requires a copyable type used with a non-copyable type |
| [SCA1022](../diagnostics/sca1022.md) | Container used as an untyped collection |
| [SCA1023](../diagnostics/sca1023.md) | Container used as an opaque type |
| [SCA1030](../diagnostics/sca1030.md) | By-value read from a stored container |
| [SCA1031](../diagnostics/sca1031.md) | Method that returns a container which it does not own, without `[return: Borrowed]` |
| [SCA1032](../diagnostics/sca1032.md) | Method that keeps a container parameter which is not marked `[Kept]` |
| [SCA1033](../diagnostics/sca1033.md) | Container moved out of something that is not a local which the method owns |
| [SCA1040](../diagnostics/sca1040.md) | A `[NonCopyable]` or `[RequiresCopyable]` mark that is not kept, a `[Borrowed]` or `[Kept]` mark that is added, or a `[MovedFrom]` mark that is added or dropped |
| [SCA1041](../diagnostics/sca1041.md) | `[MovedFrom]` used wrongly |
| [SCA1042](../diagnostics/sca1042.md) | Line of a list file that cannot be parsed, a `noncopyable` line that names no type, or a `copyable` line that has no effect |
| [SCA1043](../diagnostics/sca1043.md) | Type that derives from an erasable type and does not stay erasable |
| [SCA1044](../diagnostics/sca1044.md) | Attribute of the analyzer that is declared `[Conditional]` |
| [SCA1045](../diagnostics/sca1045.md) | A <xref:Singulink.CodeAnalysis.NonCopyableAttribute> in a namespace other than `Singulink.CodeAnalysis`, or an attribute class named `NonCopyable` with no suffix, which is not recognized: at its declaration, or at its use if a referenced assembly declares it |
| [SCA1046](../diagnostics/sca1046.md) | `[NotNullRef]` where it does not count: on a by-value parameter, on a parameter of anything but a method that is not virtual or a local function, or on a method that the compiler can leave out of a build |
| [SCA1047](../diagnostics/sca1047.md) | `[NotNullRef]` on a parameter that the project declares: the analyzer does not check the method, which is to be verified and the finding then suppressed there |
| [SCA1048](../diagnostics/sca1048.md) | Member that is nested too deeply for the analyzer, which was not checked |

## Next Steps

These guides cover the rest of what a project does with the analyzers:

- [Marking a Struct](marking-a-struct.md) - The attributes, declaring them yourself, and which names are recognized.
- [Marking a Type Parameter](marking-a-type-parameter.md) - Generics that do not copy, and what a marked type parameter allows.
- [Libraries](libraries.md) - Passing the analyzers on to the projects that use your library, and shipping a symbol list.
- [The Symbol List](symbol-list.md) - Telling the analyzer about code that you cannot mark, and the built-in list.
- [Moving a Value](moving-a-value.md) - The move helper and the `[MovedFrom]` mark.
- [Turning Rules Down](turning-rules-down.md) - The severity of a rule, and the properties of the package.

</div>
