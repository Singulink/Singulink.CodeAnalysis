<div class="article">

# Code Not Built With the Analyzer

The analyzer checks the code of the project, and of every assembly that was built with it. This page says how it tells such an assembly from one that was not, and what the rules ask of code that was not.

### At a Glance

- **Your project, a library that was built with the analyzers, and the framework need nothing from you.** The rest of the rules are about any other assembly.
- **Passing a value by value is always fine:** the value is moved, as into any callee.
- **A member that gets at a non-copyable value in any other way** gets a warning until a [symbol list entry](symbol-list-format.md) with `borrows` vouches for it (SCA1024).
- **A container that such a member hands out counts as stored,** unless a symbol list entry marks the member `new`.
- **An assembly that can use a non-copyable type of a project or a library** is reported once (SCA1025), unless a `trusted` line vouches for it.
- **Marks that grant something count as absent** on such code. Marks that only restrict are honoured from anywhere.

## Which Code Needs Anything From You

Most code that a project uses needs nothing from you:

| Assembly | How it is treated | What you have to do |
|---|---|---|
| Your project | Checked | Nothing |
| A library that was built with the analyzers | Checked in its own build, and its marks are trusted | Nothing |
| The framework | Covered by the [built-in framework symbol list](framework-symbol-list.md) | Nothing |
| Any other assembly | Gets a warning where it touches a non-copyable value in a way that could copy it | Answer the warning where it appears: build the assembly with the analyzers, or vouch for the member or the assembly with one line |

The rest of this page gives the exact rules for the last row.

**Checked code** is the compilation that is being analyzed, and every assembly that carries the marker of an analyzed build. **Unchecked code** is every other assembly: the framework, a library of a third party, and a project in the solution that turns the analyzers off.

## Marker of an Analyzed Build

The build files of the package add an assembly attribute to every project that runs the analyzers: an <xref:System.Reflection.AssemblyMetadataAttribute> with the key `Singulink.CodeAnalysis.Analyzed` and, as its value, the version of the analyzers that built the assembly, such as `0.7.0`.

- **A build that does not run analyzers gets no marker:** that is one with `RunAnalyzers` or `RunAnalyzersDuringBuild` set to `false`.
- **A project with `GenerateAssemblyInfo` set to `false`** gets nothing from the item that the package adds, and writes the attribute itself, with the version of the analyzers that it is built with as the value:

  ```cs
  [assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "0.7.5")]
  ```

  The line is only for such a project. A project that generates its assembly info gets the line from the package, and must not write it as well: the assembly would then carry two markers.

- **The marker is a claim made by the build,** like a [symbol list entry](symbol-list-format.md). A project that suppresses warnings still carries it, and the projects that use its assembly trust its marks all the same.
- **The attributes package carries it,** so the marks of <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> and of the two checks of a reference are trusted wherever they are used.

## Marks That Unchecked Code Cannot Grant

A mark that grants something promises what only a checked body can guarantee. On a member of unchecked code these marks count as absent:

| Mark | What it promises | On unchecked code |
|---|---|---|
| `[NonCopyable]` on a type parameter | The generic copies no value of it | The generic is an unmarked generic, and goes by the [symbol list](symbol-list-format.md) (SCA1020) |
| `[MovedFrom]` | The method leaves a fresh value behind | An ordinary `ref` parameter |
| `[NotNullRef]` | The method returns only for a reference that refers to something | A call proves nothing (see [Null References](null-references.md)) |

Marks that only restrict are honoured from anywhere, since a wrong one means more warnings and not fewer: `[NonCopyable]` on a struct, `[Kept]`, `[return: Borrowed]` and `[RequiresCopyable]`.

## Values Given to Unchecked Code

Generic code goes by the [symbol list](symbol-list-format.md) as before. A member whose parameter mentions a type parameter that is not marked, or has a type parameter as its type, gets a warning where it is not on the symbol list (SCA1020). It keeps what it is given unless its entry is marked `borrows`: see [Code That Nothing Checks](what-keeping-follows.md#code-that-nothing-checks).

The rule here is for a member of unchecked code whose signature names a non-copyable type concretely. **Such a member that gets at a non-copyable value in any way other than by value needs a symbol list entry with `borrows`.** Without the entry its use gets a warning (SCA1024).

```cs
ThirdParty.Leases.Inspect(ref _lease);         // SCA1024 until a symbol list file has 'M:ThirdParty.Leases.Inspect(ThirdParty.Lease@);borrows'
ThirdParty.Leases.Take(NonCopyable.Move(ref _lease));   // OK: passed by value, so moved
_lease.Release();                              // OK: a member of the non-copyable type itself
```

A container that was passed to such a member is no longer the caller's own. A method that returns it gets a warning for returning a container that something else holds (SCA1031).

#### Ways Other Than by Value

| Way | Example |
|---|---|
| By `ref`, `in` or `ref readonly` | `Inspect(ref Lease lease)` |
| Inside a container: a type that names a non-copyable type as a type argument, as an element type, or in its base types | `List<Lease>`, `Lease[]`, `class Leases : List<Lease>` |
| Inside a view | a span or a memory |
| Through a pointer, including a pointer to a pointer | `Lease*` |

A class that only declares a field of a non-copyable type names no non-copyable type in its own type, and is not a container here: see [Unchecked Code Behind a Checked Type](#unchecked-code-behind-a-checked-type).

#### What Is Free and What Is Covered

- **A value that is passed by value is always fine:** the value is moved, and is the callee's from then on. An `out` argument gives the callee nothing to read.
- **A span is no exception.** A callee cannot keep the span, but it can copy an element out of it.
- **`borrows` says what is needed:** the member uses what it is given during the call and keeps nothing of it.

#### Every Way In

The rule covers every way in which a member gets the value:

- an argument, or the receiver of an extension;
- an operand of an operator or of a conversion;
- the `Invoke` of a delegate type that unchecked code declares and that is not generic;
- the value of a setter, an `init` accessor or an indexer setter;
- the handler that `+=` or `-=` gives an event.

A member that the compiler calls for an operation, without the code naming it, is judged like the same member written out. That is a user-defined conversion of the elements of a `foreach`, a spread or a deconstruction, the conversions of a compound assignment, and the members of a `foreach`, an `await` and a pattern.

#### The Non-copyable Type's Own Members

The members of the non-copyable type itself stay trusted with values of that type: with the receiver, and with a parameter of the type itself. `handle.Free()` on a struct of a library has to work.

A member of the type that gets at another non-copyable value, or at a container, even one of its own type, is judged like any other member: `Lease.Grab(ref Guard)` needs its entry.

The [built-in framework symbol list](framework-symbol-list.md) has the members of the framework that get at one of its built-in non-copyable types in this way. A test of the analyzer scans the shared frameworks for every such member.

## Containers Handed Out by Unchecked Code

**A container that a member of unchecked code hands out is stored, unless a [symbol list entry](symbol-list-format.md) marks the member `new`.** Nothing checks that no one else holds it.

- This applies to the result of a call, and to what comes out through an `out` argument, which is stored whatever code it comes from.
- A read from it is a read from a stored container (SCA1030), and returning it is returning a container that something else holds (SCA1031). The message names the member and the `new` entry.
- The same holds for what a member that the compiler calls hands out: the `GetEnumerator` and `Current` of a `foreach` and the `GetResult` of an `await`.
- A non-copyable value that a member returns by value stays a new value: nothing could check otherwise.

```cs
Lease first = ThirdParty.Leases.Create()[0];   // SCA1030 until a symbol list file has 'M:ThirdParty.Leases.Create;new'
```

**An object of a class of unchecked code that derives from a container type, or implements one, is never new for the members that it inherits.** An example is `class Leases : List<Lease>` in such a library. Its own code decides what it holds, and nothing checks that code. No `new` entry changes that, and the message says so instead of suggesting one. This extends the rule for [a class that derives from a container on the symbol list](listed-members.md#classes-that-derive-from-a-container-on-the-symbol-list).

## Unchecked Code Behind a Checked Type

**A call is judged by the member that it binds to.** So the call does not reveal code of an unchecked assembly that runs behind a type of checked code or of the framework. There are three routes:

| Route | What the call looks like | What really runs |
|---|---|---|
| A checked class that holds a non-copyable value, given to unchecked code | `plugin.Attach(session)`, where `Session` has a readable `LockGuard` field | Unchecked code that can copy the field out of `session` |
| A checked interface or abstract class that unchecked code implements | `IGuardSink.Accept(ref guard)` | The unchecked implementation |
| A delegate or a function pointer that unchecked code made | `onGuard(ref guard)` | The unchecked method behind it |

- **The first route** needs no container: the class names no non-copyable type in its own type, so passing it by value is fine. A member that returns a reference to such a field counts the same.
- **The second route** includes an interface of the framework, such as an `IEnumerable<Lease>` whose `GetEnumerator` an unchecked class implements. The call is judged by the member of the interface, whose marks hold.
- **The third route** includes the generic delegate types of an unchecked assembly: their `Invoke` is not judged by the rule for unchecked code at all.

#### Warning for Each Route

**For each route, the unchecked code has to use the non-copyable type in its own code,** so its assembly has to reference the assembly that declares the type. That reference gets a warning, once for each unchecked assembly that the project references ([SCA1025](../diagnostics/sca1025.md)). It covers every route for a type that a project or a library declares, whether a `[NonCopyable]` mark makes it non-copyable or a `noncopyable` line for a type of checked code does.

**Some values need no such reference:** a built-in non-copyable type (the framework declares them), a struct of unchecked code that only a `noncopyable` line makes non-copyable, and a value that is bound to a marked type parameter. For those, the unchecked code still needs a way to the value. The reference that gets the warning is then one to an assembly of checked code whose members let other code get at such a value: see [Values That Checked Code Lets Out](../diagnostics/sca1025.md#values-that-checked-code-lets-out).

Build the assembly with the analyzer, or vouch for it with a line of a [symbol list file](symbol-list-format.md):

```text
A:Contoso.Plugins;trusted
```

The line turns off the warning for that assembly and nothing else: its members stay unchecked code for the rules above.

## What Stays Out of Reach

One thing remains: what is handed over or vouched for. A value passed by value is the callee's from then on, and so is what a `borrows` or a `new` entry or a `trusted` line vouches for.

How this weighs against the rest: [What Is Not Covered](not-covered.md).

</div>
