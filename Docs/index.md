<div class="article">

# Singulink.CodeAnalysis

**Singulink.CodeAnalysis** is a set of Roslyn analyzers that find every place where code copies a struct that must never be copied. A lock
guard, a handle or a pooled buffer is a struct where each instance owns something that only one instance may own: copy it, and two instances
release the same lock or free the same handle. Mark such a struct with `[NonCopyable]`, and the build warns wherever it is copied, including
the copies that are easy to miss: a read out of a list or a task, a value passed through generic code, and the copies that the compiler makes
by itself for patterns, iterators and read-only references.

The analyzers check only what is marked, and there are no style or opinion rules. All the rules so far, the SCA1000s, are about non-copyable
structs.

**Singulink.CodeAnalysis** is part of the **Singulink Libraries** collection. Visit https://github.com/Singulink/ to see our full list of publicly
available libraries and other open-source projects.

### Installation

The analyzers come in two packages on NuGet:

- `Singulink.CodeAnalysis` - the attributes and the <xref:Singulink.CodeAnalysis.NonCopyable> helpers, which bring the analyzers with them.
- `Singulink.CodeAnalysis.Analyzers` - the analyzers only, for a project that declares the attributes itself.

A project that references one of the two packages itself also has ten framework structs checked as non-copyable types, such as
<xref:System.Runtime.InteropServices.GCHandle> and <xref:System.Threading.SpinLock>. A project that only gets the analyzers through a library
does not. See [Non-copyable Types](articles/concepts/non-copyable-types.md).

**Requirements**: C# projects built with the .NET 10 SDK or later.

## Information and Links

Here are some additional links to get you started:

- [Getting Started](articles/guides/getting-started.md) - Visit here first for a walkthrough. It opens the guides: an article on every part of using the analyzers, listed in the sidebar.
- [New Values and Stored Values](articles/concepts/new-and-stored-values.md) - The one distinction that every rule comes down to, and the start of the concepts behind the rules.
- [SCA1001](articles/diagnostics/sca1001.md) - The first of the diagnostics: an article for every ID, with what the finding means and how to fix it.
- [API Documentation](api/Singulink.CodeAnalysis.yml) - Browse the attributes and the helpers here.
- [Chat on Discord](https://discord.gg/EkQhJFsBu6) - Have questions or want to discuss the library? This is the place for all Singulink project discussions.
- [Github Repo](https://github.com/Singulink/Singulink.CodeAnalysis) - File issues, contribute pull requests or check out the code for yourself!

</div>
