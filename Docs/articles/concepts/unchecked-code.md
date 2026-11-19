<div class="article">

# Code Not Built With the Analyzer

The analyzer checks the code of the project, and of every assembly that was built with it. This article says how it tells such an assembly
from one that was not, and what the rules ask of code that was not.

### The Marker

The build files of the package add an assembly attribute to every project that runs the analyzers: an
<xref:System.Reflection.AssemblyMetadataAttribute> with the key `Singulink.CodeAnalysis.Analyzed` and the value `1`, the version of the
marker. An assembly was built with the analyzer if it is the compilation that is being analyzed, or if it carries the marker. Every other
assembly is unchecked code: the framework, a library of a third party, and a project of the solution that turns the analyzers off.

- **A build that does not run analyzers gets no marker,** one with `RunAnalyzers` or `RunAnalyzersDuringBuild` set to `false`.
- **A project with `GenerateAssemblyInfo` set to `false`** gets nothing from the item that the package adds, and writes the attribute
  itself:

  ```cs
  [assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "1")]
  ```

- **The marker is a statement of the build,** like a line of a [list file](../guides/symbol-list.md). A project that suppresses findings
  still carries it, and the projects that use its assembly trust its marks all the same.
- **The attributes package carries it,** so the marks of <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> and
  of the two checks of a reference are trusted wherever they are used.

## Marks That Unchecked Code Cannot Grant

A mark that grants something promises what only a checked body can hold: `[NonCopyable]` on a type parameter says that the generic copies
no value of it, `[MovedFrom]` that the method leaves a fresh value behind, `[NotNullRef]` that the method returns only for a reference that
refers to something. On a member of unchecked code these marks count as absent. A generic whose type parameter is marked there is an
unmarked generic and goes by the list (`SCA1020`), a `[MovedFrom]` parameter there is an ordinary `ref` parameter, and a call of a method
whose parameter is marked `[NotNullRef]` there proves nothing (see [Null References](../guides/null-references.md)).

Marks that only restrict are honoured from anywhere, since a wrong one means more findings and not fewer: `[NonCopyable]` on a struct,
`[Kept]`, `[return: Borrowed]` and `[RequiresCopyable]`.

## Values Given to Unchecked Code

Generic code goes by the list as before: a member whose parameter mentions a type parameter that is not marked, or has a type parameter as
its type, is reported where it is not listed (`SCA1020`), and keeps what it is given unless the list marks it `borrows` (see
[Keeping a Container That Is Passed In](keeping-containers.md)). The rule here is for a member of unchecked code whose signature names a
non-copyable type concretely, which those rules do not reach.

**A member of unchecked code that gets at a non-copyable value in any way other than by value needs a list entry with `borrows`.** Any
other way is by `ref`, `in` or `ref readonly`, inside a container, inside a view (a span or a memory) and through a pointer, a pointer
at a pointer included. A container here is a type that names a non-copyable type: as a type argument (`List<Lease>`), as the element type
of an array (`Lease[]`), or in what it derives from or implements (`class Leases : List<Lease>`). A class that only declares a field of a
non-copyable type names none, and is not one: see [What Stays Out of Reach](#what-stays-out-of-reach). Without the entry the use of the
member is reported (`SCA1024`), and a container that was passed to it is no longer the caller's own: a method that returns it is reported
as returning a container that something else holds (`SCA1031`).

- **A value that is passed by value is free:** the value is moved, and is the callee's business from then on. An `out` argument gives the
  callee nothing to read.
- **The rule covers every way in which a member gets the value:** an argument, the receiver of an extension, an operand of an operator or
  of a conversion, the `Invoke` of a delegate type that unchecked code declares and that is not generic, the value of a setter, an `init` accessor or an indexer
  setter, and the handler that `+=` or `-=` gives an event. A member that the compiler calls for an operation, with nothing written for
  it, is judged as the member written out: a user-defined conversion of the elements of a `foreach`, a spread or a deconstruction, the
  conversions of a compound assignment, and the members of a `foreach`, an `await` and a pattern.
- **The members of the non-copyable type itself stay trusted with values of that type:** with the receiver, and with a parameter of the
  type itself. `handle.Free()` on a struct of a library has to work. A member of the type that gets at another non-copyable value, or at a
  container even of its own type, is judged like any other member: `Lease.Grab(ref Guard)` needs its entry.
- **A span is no exception.** A callee cannot keep the span, but it can copy an element out of it.
- **`borrows` says what is needed:** the member uses what it is given during the call and keeps nothing of it.

```cs
ThirdParty.Leases.Inspect(ref _lease);         // SCA1024 until the list has 'M:ThirdParty.Leases.Inspect(ThirdParty.Lease@);borrows'
ThirdParty.Leases.Take(NonCopyable.Move(ref _lease));   // OK: passed by value, so moved
_lease.Release();                              // OK: a member of the non-copyable type itself
```

The built-in list has the members of the framework that get at one of its ten non-copyable types in this way, and a test of the
analyzer scans the shared frameworks for every such member: see [What Is Built In](../guides/symbol-list.md#what-is-built-in).

## Containers Handed Out by Unchecked Code

**A container that a member of unchecked code hands out is stored, unless a list entry marks the member `new`.** Nothing checks that
nothing else holds it. That goes for the result of a call and for what comes out through an `out` argument, which is stored from any code.
A read from it is a read from a stored container (`SCA1030`), and returning it is returning a container that something else holds
(`SCA1031`); the message names the member and the `new` entry. A member that the compiler calls hands out in the same way: the
`GetEnumerator` and `Current` of a `foreach` and the `GetResult` of an `await`. A non-copyable value that a member returns by value stays
a new value: nothing could check otherwise.

**An object of a class of unchecked code that derives from a container type, or implements one, is never a new container for the members
that it inherits,** as `class Leases : List<Lease>` in such a library is. Its own code decides what it holds, and nothing checks that code.
No `new` entry changes that, and the message says so instead of suggesting one. This extends the rule for a class that derives from a
listed container: see [What a Listed Member Hands Out](listed-members.md#classes-that-derive-from-a-listed-container).

```cs
Lease first = ThirdParty.Leases.Create()[0];   // SCA1030 until the list has 'M:ThirdParty.Leases.Create;new'
```

## Unchecked Code Behind a Checked Type

**A call is judged by the member that it binds to, so code of an unchecked assembly that runs behind a type of checked code or of the
framework is not seen at the call.** These are the routes:

- **A class of checked code that holds a non-copyable value, given to unchecked code.** A class with a field of a non-copyable type that
  another assembly can read, or with a member that returns a reference to one, names no non-copyable type in its own type, so passing an
  object of it by value is free, and the unchecked member can copy the value out of it.
- **An interface or an abstract class of checked code that unchecked code implements.** A call through the interface or the base class is
  judged by its member, whose marks hold, while the code that runs is the implementation of the unchecked assembly. The same goes for an
  interface of the framework, such as an `IEnumerable<Lease>` whose `GetEnumerator` an unchecked class implements.
- **A delegate or a function pointer that unchecked code made.** Its invocation is judged by the delegate type, or the function pointer
  type, which says nothing of the method behind it. The generic delegate types of an unchecked assembly are among them: their `Invoke` is
  not judged by the rule for unchecked code at all.

**Each route needs the unchecked code to name the non-copyable type, and so its assembly to reference the assembly that declares it.**
That reference is reported, once for each unchecked assembly that the project references ([SCA1025](../diagnostics/sca1025.md)). It
covers every route for a type that a project or a library declares: one that a `[NonCopyable]` mark makes non-copyable, or a
`noncopyable` line of a list file where the type is of checked code. Build the assembly with the analyzer, or vouch for it with a line
`A:Contoso.Plugins;trusted` of a list file. The line turns off the finding for that assembly and nothing else: its members stay unchecked
code for the rules above.

## What Stays Out of Reach

Unchecked code can copy what it is given, and no rule sees that. A value that is passed to it by value is its business from then on, and
so is what a `borrows` or a `new` entry vouches for, or a `trusted` line. A base class of unchecked code that clones itself copies the
fields of a derived class of the project: see [What Is Not Covered](not-covered.md).

**The routes behind a checked or a framework type stay out of reach for the built-in non-copyable types,** such as
<xref:System.Runtime.InteropServices.GCHandle> and <xref:System.Threading.SpinLock>. Every assembly references the framework, so the
reference says nothing, and no finding is given for it. The same goes for a struct of unchecked code that only a `noncopyable` line of a
list file makes non-copyable: a line for a struct of the framework would otherwise report every assembly of the framework.

</div>
