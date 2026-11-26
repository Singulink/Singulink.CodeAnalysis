<div class="article">

# Marking a Struct

A struct whose instances each own something, such as a lock, a handle or a rented buffer, is marked `[NonCopyable]`. This page shows the two ways to get the attribute, the mistakes in declaring one that the analyzer warns about, and how to say that a copy of a struct is unsafe.

### At a Glance

| Way | Reference | You get |
|---|---|---|
| Use the attribute from the package | `Singulink.CodeAnalysis` | The six attributes, `NonCopyable.Move` and the two null checks |
| Declare the attributes yourself | `Singulink.CodeAnalysis.Analyzers` only | No runtime dependency; you write your own move and checks |

Most structs need only `[NonCopyable]`. A struct whose copy can free, unpin or reach memory twice also says `UnsafeToCopy = true`: see [Unsafe to Copy](#unsafe-to-copy).

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

> [!TIP]
> **Reference `Singulink.CodeAnalysis` unless the runtime dependency is a problem.** An update of the package brings the attributes, the helpers and the analyzer together. Attributes that you declare stay as you wrote them, so a property or an attribute that an update of the analyzer adds is yours to add as well.

The analyzer recognizes its attributes by their full name: the namespace `Singulink.CodeAnalysis` and the name of the class, in any assembly, public or internal. So a project that wants no runtime dependency declares them itself, in that namespace, and references only `Singulink.CodeAnalysis.Analyzers`:

```cs
namespace Singulink.CodeAnalysis;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
internal sealed class NonCopyableAttribute : Attribute
{
    public bool UnsafeToCopy { get; set; }
}

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

Declare only the ones that you use. The analyzer reads the arguments of an attribute and never calls it, so your `NonCopyableAttribute` needs only a settable `bool` property or a public `bool` field named `UnsafeToCopy` for `UnsafeToCopy = true` or `UnsafeToCopy = false` to count. Leave it out where no struct of the project is [unsafe to copy](#unsafe-to-copy) and no type parameter says `UnsafeToCopy = false`.

This works across assemblies: a struct that a referenced assembly marks with its own internal attribute is non-copyable in your code as well, and unsafe to copy where that attribute says so.

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

## Unsafe to Copy

A copy of most non-copyable structs breaks the logic of the program: a lock is released twice, or a count goes down twice. A copy of some breaks memory safety. Ask one question of your struct: **would a copy free, unpin or reach memory twice, or after it is gone?** Where the answer is yes, say so:

```cs
using System.Runtime.InteropServices;
using Singulink.CodeAnalysis;

[NonCopyable(UnsafeToCopy = true)]
public struct NativeBuffer : IDisposable
{
    private nint _memory;

    public static NativeBuffer Allocate(int size) => new() { _memory = Marshal.AllocHGlobal(size) };

    public void Dispose()
    {
        Marshal.FreeHGlobal(_memory);
        _memory = 0;
    }
}
```

A copy of `NativeBuffer` frees the memory a second time, or reads it after the other copy freed it. Some examples of each side:

| Struct | Unsafe to copy? | What a copy does |
|---|---|---|
| A lock guard | No | Releases the lock twice: a logic error |
| An array rented from `ArrayPool<T>` | No | Returns the array twice, so two later renters can share it. That shares data, but every access stays inside a managed array |
| A block of native memory that `Dispose` frees | Yes | Frees the memory twice, or reads it after it is freed |
| A `GCHandle` that pins an array, with a pointer into it | Yes | Unpins the array while the pointer is still in use |

Under the current memory safety rules the property changes nothing: every copy of a non-copyable struct gets the same warning. Under the [updated memory safety rules](../rules/updated-memory-safety-rules.md) a copy of a struct that is unsafe to copy is an error, which only an unsafe context or a declaration marked `safe` or `unsafe` can answer for. A copy of any other non-copyable struct gets the ordinary warning there, as under the current rules.

A nullable, a tuple or a generic struct over such a type is unsafe to copy as well, such as `NativeBuffer?` or `(NativeBuffer, int)`. Nothing needs to say so: the analyzer reads it from the type arguments.

#### A Struct That Holds One

A struct that holds a value that is unsafe to copy in a field is unsafe to copy too, since a copy of it copies the field. Say so on its own attribute. The analyzer reads it from the declaration of the struct and never works it out from the fields, so without the property each such field gets a warning (SCA1017):

```cs
[NonCopyable]
public struct Session
{
    private NativeBuffer _buffer;               // SCA1017: a copy of Session copies the buffer
}

[NonCopyable(UnsafeToCopy = true)]
public struct OwnedSession
{
    private NativeBuffer _buffer;               // OK
}
```

Until the struct says so, it is what its attribute says: a copy of `Session` is a plain non-copyable copy. What each kind of field asks of the struct:

| Field | Asks of the struct |
|---|---|
| `NativeBuffer`, `NativeBuffer?`, `(NativeBuffer, int)`, or another struct that is unsafe to copy | `UnsafeToCopy = true` |
| `NativeBuffer[]`, a class, or a `List<NativeBuffer>` | Nothing: the field holds the value by reference |
| A type parameter of the struct, or a type over one such as `T?` | Nothing: each constructed type of the struct is judged by its type arguments |

A struct with such a field that is not marked `[NonCopyable]` at all gets SCA1010, whose message names `[NonCopyable(UnsafeToCopy = true)]`, so one edit fixes both. Under the current memory safety rules SCA1017 is an ordinary warning; under the updated rules it is an error.

## Next Steps

Here is where to go from here:

- [Marking a Type Parameter](marking-a-type-parameter.md) - Generic types that hold your structs.
- [Sharing Your Library](libraries.md) - Letting the analyzer reach the projects that use your structs.

</div>
