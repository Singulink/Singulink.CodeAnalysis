<div class="article">

# Marking a Struct

How to mark a struct as non-copyable, and how the analyzer recognizes its attributes.

## Two Ways to Mark a Struct

The analyzer recognizes its six attributes **by their full name: the namespace `Singulink.CodeAnalysis` and the name of the class, in any
assembly, public or internal.** There are two ways to mark a struct:

- Reference `Singulink.CodeAnalysis` and use its `[NonCopyable]` attribute, as in the example above.
- Declare the attributes yourself, in that namespace, and reference only `Singulink.CodeAnalysis.Analyzers`:

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

This works across assemblies: a struct that a referenced assembly marks with its own internal attribute is non-copyable in your code as well. The same
holds for the other five attributes. Declare only the ones that you use. The <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> helper is only in the `Singulink.CodeAnalysis`
package; a project that declares its own attributes [writes its own](moving-a-value.md). The two checks of a reference,
<xref:Singulink.CodeAnalysis.NonCopyable.ThrowIfNullRef*?displayProperty=nameWithType> and <xref:Singulink.CodeAnalysis.NonCopyable.ThrowIfNullRefArgument*?displayProperty=nameWithType>, are in the package as well, and the analyzer does not know them
by name: a project that declares its own attributes declares `[NotNullRef]` and writes whatever check it likes, as in
[A reference that is known to refer to something](null-references.md).

## How the Attributes Are Recognized

**The namespace is part of the name.** A class called <xref:Singulink.CodeAnalysis.KeptAttribute> or <xref:Singulink.CodeAnalysis.BorrowedAttribute> in any other namespace belongs to something
else, another tool's attribute perhaps, and has no effect here: no other namespace is recognized, `Microsoft.CodeAnalysis` included. For five
of the six that fails on the safe side, with more findings. For <xref:Singulink.CodeAnalysis.NonCopyableAttribute> it does not: a struct that is marked with a class of
that name in another namespace is copyable, in a build that looks clean. So **an attribute class named <xref:Singulink.CodeAnalysis.NonCopyableAttribute> in any other
namespace is reported where it is declared (`SCA1045`).** Move it to `Singulink.CodeAnalysis`, or suppress the finding if the attribute is
meant for something else, such as another analyzer that runs next to this one. A project that marks its own struct or type parameter with
such an attribute out of a referenced assembly has no declaration to report, so there the finding is at the use. A class of the name that
does not derive from <xref:System.Attribute> marks nothing and is not reported.

**The suffix is part of the name too.** `[NonCopyable]` finds an attribute class named `NonCopyable`, with no suffix, just the same, and
the analyzer does not recognize it. Such a class is reported like one named <xref:Singulink.CodeAnalysis.NonCopyableAttribute> in another namespace, in every
namespace, `Singulink.CodeAnalysis` included (`SCA1045`): name it <xref:Singulink.CodeAnalysis.NonCopyableAttribute>. The helper class `NonCopyable` is no attribute.

**One exception, for code that is not yours to change: on a struct of a referenced assembly, a <xref:Singulink.CodeAnalysis.NonCopyableAttribute> of any namespace is
recognized.** Libraries that are already published mark their guards with an attribute of their own, most often an internal
`Microsoft.CodeAnalysis.NonCopyableAttribute` after the convention of the Roslyn analyzer. Their guards are non-copyable in your project as
they were meant to be. A referenced project counts as such an assembly: in its own build its attribute is reported, and for the projects
that use it the mark holds. The exception does not reach a type parameter of a referenced generic, since a mark there says that the body of
the generic was checked by this analyzer, and it does not reach the other four attributes.

**Do not make such an attribute `[Conditional]`.** The compiler leaves a conditional attribute out of the assembly unless its symbol is defined.
The analyzer still sees every use of it in the source of the project that declares it, so the library builds as if its marks held, and for every
project that uses the library they are gone: its structs can be copied there with no warning. A conditional attribute with one of the five names
is reported where it is declared (`SCA1044`). A project that uses such an attribute from a referenced assembly is told at every use that the
compiler drops: the mark holds in that project and is missing from the assembly that it builds. <xref:System.Diagnostics.ConditionalAttribute> is inherited, so an
attribute that derives from a conditional class is conditional too, and is reported the same way.

**A class that derives from <xref:Singulink.CodeAnalysis.NonCopyableAttribute> marks like it.** `sealed class LockGuardAttribute : NonCopyableAttribute` gives the mark
a name of its domain, on a struct and on a type parameter, and on a struct of a referenced assembly for a base class of that name in any
namespace. This holds for <xref:Singulink.CodeAnalysis.NonCopyableAttribute> only: a class that derives from one of the other four is not recognized, which means more
findings and not fewer.

</div>
