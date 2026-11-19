<div class="article">

# Marking a Struct

A struct whose instances each own something, such as a lock, a handle or a rented buffer, is marked `[NonCopyable]`. This page shows the two ways to get the attribute, and the mistakes in declaring one that the analyzer warns about.

### At a Glance

| Way | Reference | You get |
|---|---|---|
| Use the attribute from the package | `Singulink.CodeAnalysis` | The six attributes, `NonCopyable.Move` and the two null checks |
| Declare the attributes yourself | `Singulink.CodeAnalysis.Analyzers` only | No runtime dependency; you write your own move and checks |

## Six Attributes

`[NonCopyable]` is the one that every project uses. The other five are for library authors who write generics, move methods or APIs that pass containers:

| Attribute | Goes on | Says | Explained in |
|---|---|---|---|
| `[NonCopyable]` | A struct, or a type parameter | Copies of the struct get a warning; a generic copies no value of the type parameter | This page, and [Marking a Type Parameter](marking-a-type-parameter.md) |
| `[RequiresCopyable]` | A method, a constructor, a property or an event | The member copies, so using it with a non-copyable type gets a warning | [Marking a Type Parameter](marking-a-type-parameter.md) |
| `[MovedFrom]` | A `ref` parameter | Reading it by value is a move, and the method resets it | [Writing a Move Method](writing-a-move.md) |
| `[return: Borrowed]` | A return value | The method still holds the container that it returns | [Containers in Your API](containers-in-your-api.md) |
| `[Kept]` | A parameter | The method keeps the container that it is given | [Containers in Your API](containers-in-your-api.md) |
| `[NotNullRef]` | A `ref`, `in` or `out` parameter | The method returns only if the reference refers to something | [Writing a Move Method](writing-a-move.md#references-can-be-null) |

## Using the Package

Reference `Singulink.CodeAnalysis` and mark the struct, as in [Getting Started](../start/getting-started.md#marking-a-first-struct):

```cs
using Singulink.CodeAnalysis;

[NonCopyable]
public struct LockGuard : IDisposable
{
    // ...
}
```

Let `Dispose` clear the struct, so that a second call on the same instance does nothing: the analyzer guarantees one instance, not one call of `Dispose`. See [What Is Guaranteed](../start/what-it-does.md#what-is-guaranteed).

## Declaring the Attributes Yourself

The analyzer recognizes its attributes by their full name: the namespace `Singulink.CodeAnalysis` and the name of the class, in any assembly, public or internal. So a project that wants no runtime dependency declares them itself, in that namespace, and references only `Singulink.CodeAnalysis.Analyzers`:

```cs
namespace Singulink.CodeAnalysis;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
internal sealed class NonCopyableAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Event,
    Inherited = false)]
internal sealed class RequiresCopyableAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class MovedFromAttribute : Attribute;

[AttributeUsage(AttributeTargets.ReturnValue, Inherited = false)]
internal sealed class BorrowedAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class KeptAttribute : Attribute;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class NotNullRefAttribute : Attribute;
```

Declare only the ones that you use. This works across assemblies: a struct that a referenced assembly marks with its own internal attribute is non-copyable in your code as well.

Such a project also writes for itself what the package has besides the attributes:

- <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>: see [Writing a Move Method](writing-a-move.md).
- <xref:Singulink.CodeAnalysis.NonCopyable.ThrowIfNullRef*?displayProperty=nameWithType> and <xref:Singulink.CodeAnalysis.NonCopyable.ThrowIfNullRefArgument*?displayProperty=nameWithType>: the analyzer does not know them by name, so a project declares `[NotNullRef]` and writes whatever check it likes. See [Checks in the Package](../rules/null-references.md#checks-in-the-package).

## A Name of Your Domain

A class that derives from `NonCopyableAttribute` marks a struct just as `NonCopyableAttribute` does, so a mark can carry a name from your domain. The attribute in the package is sealed, so this needs a `NonCopyableAttribute` that the project declares itself and does not seal:

```cs
namespace Singulink.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
    internal class NonCopyableAttribute : Attribute;
}

sealed class LockGuardAttribute : NonCopyableAttribute;

[LockGuard]
struct Lease { }
```

This holds for `NonCopyableAttribute` only: a class that derives from one of the other five attributes is not recognized.

## Mistakes the Analyzer Warns About

> [!WARNING]
> Two mistakes leave structs copyable in a build that looks clean:
>
> - **A `NonCopyableAttribute` that your project declares in any other namespace** marks nothing. You get a warning where such a class is declared (SCA1045), and also where a class named `NonCopyable`, with no suffix, is declared in any namespace.
> - **An attribute that is `[Conditional]`** is dropped from the assembly, so the library builds as if its marks held, and every project that uses it can copy its structs with no warning. You get a warning where such an attribute is declared (SCA1044).

A library that is already published, with its guards marked by a `NonCopyableAttribute` of another namespace, such as `Microsoft.CodeAnalysis`, still works: on a struct of a referenced assembly, the analyzer recognizes that attribute in any namespace.

The exact rules: [How the Attributes Are Recognized](../rules/attribute-recognition.md).

## Next Steps

Here is where to go from here:

- [Marking a Type Parameter](marking-a-type-parameter.md) - Generic types that hold your structs.
- [Sharing Your Library](libraries.md) - Letting the analyzer reach the projects that use your structs.

</div>
