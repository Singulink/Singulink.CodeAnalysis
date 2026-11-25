<div class="article">

# Singulink.CodeAnalysis

**Singulink.CodeAnalysis** is a set of Roslyn analyzers that find every place where code copies a struct that must never be copied.

A lock guard, a handle or a pooled buffer is a struct where each instance owns something that only one instance may own. Copy it, and two instances release the same lock or free the same handle. Mark such a struct with `[NonCopyable]`, and the build warns wherever it is copied, even where the copy is easy to miss:

- a read out of a list or a task;
- a value passed through generic code;
- the copies that the compiler makes by itself for patterns, iterators and read-only references.

Moving a value is not a copy, because one instance is left afterwards. A local that you pass on and do not use again moves, and
<xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> takes a value out of a field and leaves the field empty. See
[Moving Values](articles/guides/moving-values.md).

The analyzers check only what is marked, and there are no style or opinion rules. All the rules so far, the SCA1000s, are about non-copyable
structs.

**Singulink.CodeAnalysis** is part of the **Singulink Libraries** collection. Visit https://github.com/Singulink/ to see our full list of publicly
available libraries and other open-source projects.

### Installation

The analyzers come in two packages on NuGet:

- `Singulink.CodeAnalysis` - the attributes and the <xref:Singulink.CodeAnalysis.NonCopyable> helpers, which bring the analyzers with them.
- `Singulink.CodeAnalysis.Analyzers` - the analyzers only, for a project that declares the attributes itself.

In a project that references one of the two packages directly, the analyzers also treat some framework structs as non-copyable, such as
<xref:System.Runtime.InteropServices.GCHandle> and <xref:System.Threading.SpinLock>. In a project that gets the analyzers only through a
library, they do not. See [Built-in Types](articles/guides/built-in-types.md).

**Requirements**: C# projects built with the .NET 10 SDK or later.

## Information and Links

Here are some additional links to get you started:

- [What the Analyzer Does](articles/start/what-it-does.md) - Visit here first. It opens Start Here, a few short pages to read in order.
- [Why Am I Seeing This?](articles/start/why-am-i-seeing-this.md) - For a project that got the analyzers through a library that it uses.
- [New and Stored Values](articles/start/new-and-stored-values.md) - The one distinction that every rule comes down to.
- [All Diagnostics](articles/all-diagnostics.md) - Every ID, each with an article on what the warning means and how to fix it.
- [API Documentation](api/Singulink.CodeAnalysis.yml) - Browse the attributes and the helpers here.
- [Chat on Discord](https://discord.gg/EkQhJFsBu6) - Have questions or want to discuss the library? This is the place for all Singulink project discussions.
- [Github Repo](https://github.com/Singulink/Singulink.CodeAnalysis) - File issues, contribute pull requests or check out the code for yourself!

</div>
