<div class="article">

# Singulink.CodeAnalysis

**Singulink.CodeAnalysis** is a set of Roslyn analyzers that check code against rules you opt into with attributes. There are no style or opinion
rules. A project that gets the analyzers only through a library that uses them sees findings only where it uses what that library marks. A
project that references one of the two packages itself also gets ten framework structs, such as <xref:System.Runtime.InteropServices.GCHandle>
and <xref:System.Threading.SpinLock>, as non-copyable types.

The first set of rules, the SCA1000s, reports code that copies a struct which must not be copied, such as a lock guard, a handle, or any other
struct where each instance owns something that only one copy may own. Mark the struct with `[NonCopyable]`, and copying it becomes a warning,
also where the copy is made by a collection, a task or other generic code.

**Singulink.CodeAnalysis** is part of the **Singulink Libraries** collection. Visit https://github.com/Singulink/ to see our full list of publicly
available libraries and other open-source projects.

### Installation

The analyzers come in two packages on NuGet:

- `Singulink.CodeAnalysis` - the attributes and the <xref:Singulink.CodeAnalysis.NonCopyable> helpers, which bring the analyzers with them.
- `Singulink.CodeAnalysis.Analyzers` - the analyzers only, for a project that declares the attributes itself.

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
