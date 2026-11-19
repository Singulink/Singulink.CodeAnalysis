<div class="article">

# How the Attributes Are Recognized

The analyzer reads six attributes. This page gives the exact rule for when a class is one of them, and the two mistakes in declaring one that it warns about. [Marking a Struct](../guides/marking-a-struct.md) shows how to use them.

### At a Glance

The six attributes are <xref:Singulink.CodeAnalysis.NonCopyableAttribute>, <xref:Singulink.CodeAnalysis.RequiresCopyableAttribute>, <xref:Singulink.CodeAnalysis.MovedFromAttribute>, <xref:Singulink.CodeAnalysis.BorrowedAttribute>, <xref:Singulink.CodeAnalysis.KeptAttribute> and <xref:Singulink.CodeAnalysis.NotNullRefAttribute>.

| Attribute class | Recognized? |
|---|---|
| One of the six names, directly in the namespace `Singulink.CodeAnalysis`, in any assembly, public or internal | Yes |
| One of the six names in any other namespace, `Microsoft.CodeAnalysis` included | No. For `NonCopyableAttribute` you get a warning (SCA1045) |
| A class named `NonCopyable`, with no suffix, in any namespace | No, and you get a warning (SCA1045) |
| A class that derives from `NonCopyableAttribute` | Yes: it marks a struct just as `NonCopyableAttribute` does |
| A class that derives from one of the other five | No |
| `NonCopyableAttribute` of any namespace, on a struct of a referenced assembly | Yes |

## By Full Name

The analyzer recognizes its attributes **by their full name: the namespace `Singulink.CodeAnalysis` and the name of the class, in any assembly, public or internal.** So a project can reference the attributes package, or declare the attributes itself in that namespace and reference only the analyzers package.

This works across assemblies. A struct that a referenced assembly marks with its own internal attribute is non-copyable in your code as well, and the same holds for the other five attributes.

## The Namespace Is Part of the Name

A class called `KeptAttribute` or `BorrowedAttribute` in any other namespace belongs to something else, another tool's attribute perhaps, and has no effect here. No other namespace is recognized, `Microsoft.CodeAnalysis` included.

For five of the six, that fails on the safe side: the mark is missing, so there are more warnings and not fewer. For `NonCopyableAttribute` it does not. A struct that is marked with a class of that name in another namespace is copyable, in a build that looks clean.

> [!WARNING]
> An attribute class named `NonCopyableAttribute` in any other namespace gets a warning where it is declared (SCA1045). Move it to `Singulink.CodeAnalysis`, or suppress the warning if the attribute is meant for something else, such as another analyzer that runs next to this one.

- Where the wrong attribute comes from a referenced assembly, there is no declaration in your project to warn about. So SCA1045 appears where your code uses it, on your own struct or type parameter.
- A class with that name that does not derive from <xref:System.Attribute> marks nothing and gets no warning.

```cs
namespace Contoso.Tools;

internal sealed class NonCopyableAttribute : Attribute;   // SCA1045: the analyzer does not read this one
```

## The Suffix Is Part of the Name

C# lets you write `[NonCopyable]` for a class named either `NonCopyableAttribute` or `NonCopyable`. The analyzer recognizes only the first. A class named `NonCopyable` that derives from <xref:System.Attribute> gets a warning in every namespace, `Singulink.CodeAnalysis` included (SCA1045): rename it to `NonCopyableAttribute`.

```cs
namespace Singulink.CodeAnalysis;

internal sealed class NonCopyable : Attribute;   // SCA1045: name it NonCopyableAttribute
```

The helper class <xref:Singulink.CodeAnalysis.NonCopyable> is not an attribute, and gets no warning.

## Structs of a Referenced Assembly

One exception is for code that is not yours to change: **on a struct of a referenced assembly, a `NonCopyableAttribute` of any namespace is recognized.**

Libraries that are already published mark their guards with an attribute of their own, most often an internal `Microsoft.CodeAnalysis.NonCopyableAttribute` following the convention of the Roslyn analyzer. Their guards are non-copyable in your project as they were meant to be. A class named `NonCopyable` with no suffix counts there in the same way, since no warning can be given in the referenced assembly.

- **A referenced project counts as such an assembly.** In its own build, its attribute gets a warning; in the projects that use it, the mark holds.
- **The exception does not reach a type parameter** of a referenced generic. A mark there says that this analyzer checked the body of the generic.
- **It does not reach the other five attributes.**

## Conditional Attributes

**Do not make such an attribute `[Conditional]`.** The compiler leaves a conditional attribute out of the assembly unless its symbol is defined. The analyzer still sees every use of it in the source of the project that declares it, so the library builds as if its marks held. In every project that uses the library, the marks are gone: its structs can be copied there with no warning.

```cs
namespace Singulink.CodeAnalysis;

[Conditional("DEBUG")]                                         // SCA1044: the mark is gone from a release build
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter)]
internal sealed class NonCopyableAttribute : Attribute;
```

- A conditional attribute with one of the six names gets a warning where it is declared (SCA1044).
- A project that uses such an attribute from a referenced assembly gets a warning at every use that the compiler drops: the mark holds in that project, but is missing from the assembly that it builds.
- <xref:System.Diagnostics.ConditionalAttribute> is inherited, so an attribute that derives from a conditional class is conditional too, and gets the same warning.

## Attributes That Derive From NonCopyableAttribute

**A class that derives from `NonCopyableAttribute` marks a struct just as `NonCopyableAttribute` does.** `sealed class LockGuardAttribute : NonCopyableAttribute` gives the mark a name from your domain. It marks a struct and a type parameter. On a struct of a referenced assembly, it also marks when its base class has that name in any namespace. The attribute in the package is sealed, so this is for a project that declares its own `NonCopyableAttribute` and leaves it unsealed.

This holds for `NonCopyableAttribute` only. A class that derives from one of the other five is not recognized, which means more warnings and not fewer.

</div>
