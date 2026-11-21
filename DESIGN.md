# Design Notes

This file is for someone who has read the [documentation](https://www.singulink.com/Docs/Singulink.CodeAnalysis/index.html) and now has
to change the analyzer. The [Rules](Docs/articles/rules/index.md) articles say what the analyzer does, and the tests pin it. This file
says what neither of them says: why the rules are the way they are, what was tried and given up, what was accepted as a cost, and how
the code and the tests are laid out.

It does not restate the rules. Where a rule is named here, follow the link for the rule itself.

## Contents

- [Principles](#principles)
- [Values and Moves](#values-and-moves)
- [Generics](#generics)
- [Containers](#containers)
- [Conversions and Boxes](#conversions-and-boxes)
- [Attributes and Marks](#attributes-and-marks)
- [Code Not Built With the Analyzer](#code-not-built-with-the-analyzer)
- [Symbol List](#symbol-list)
- [Alternatives That Were Given Up](#alternatives-that-were-given-up)
- [Warnings on Correct Code That Were Accepted](#warnings-on-correct-code-that-were-accepted)
- [Limits That Were Accepted](#limits-that-were-accepted)
- [Deferred and Open](#deferred-and-open)
- [Working on the Analyzer](#working-on-the-analyzer)

## Principles

Three principles were set at the start, and most later decisions follow from them.

**Exact where the author opts in.** A generic that marks its type parameter with `[NonCopyable]` is checked by the same rules as any
other code, so what it hands out can be trusted.

**Conservative everywhere else.** Code that cannot see that a type is non-copyable is assumed to copy it, and its use gets a warning
unless the symbol list says that it is safe. Nothing is guessed from a signature. A `ref T` indexer can return a reference into a copy,
and a `void` method can act on a copy of what it stores, so a signature does not show whether a member copies.

**Moves are written, or are the last use of a variable.** Handing a value over is written as a call that resets the source
(`NonCopyable.Move`), or is the last use of a local. Finding the last use is the one job of flow analysis in the analyzer. A second use
of a moved-from variable finds a default value, not a second guard.

Two more principles grew out of the work.

**Every "this is fine" is said by naming what is allowed.** A rule that lists what is forbidden misses the forms that nobody thought
of. So each rule lists what is allowed, and a form that it does not know gets a warning instead of passing. Every "this is fine" of
the analyzer is made this way, down to the cases that have no rule of their own: an operation that the analyzer has no judgment for,
used by value, counts as a stored value; code nested too deeply gets SCA1048; and a `noncopyable` line of a symbol list file that names
nothing gets a warning.
The rules that could have been lists of what is forbidden are in [Alternatives That Were Given Up](#alternatives-that-were-given-up).

**A member that was not checked never looks clean.** The analyzer walks expressions by recursion. Where the stack would run out, it
stops and gives the member one warning, SCA1048, instead of skipping it or failing with an exception. An exception of the analyzer is a
test failure, in every test and for every generated input.

One exception goes the other way: **code that does not compile is allowed.** The analyzer runs while code is typed, and a half-written
line would otherwise get a warning next to the compiler's error.

## Values and Moves

#### New and Stored Values

The rules come down to one question: can anybody else still hold this value? A [new value](Docs/articles/rules/new-and-stored-values.md)
is one that nobody else holds, so it can go anywhere once. A stored value is one that something else still holds, so a by-value read of
it is a copy.

The by-value result of a call to checked code is a new value. The check of the callee's body guarantees it: a body that returned a
stored value would itself get a warning. So a member that takes a value out of somewhere needs no annotation, only a move, and a
caller can trust the result.

A container read from a property counts as stored, like one read from a field. A property exposes state, and only a method that returns
a container is taken to hand it over.

#### Last Use

A variable that is never used again cannot be the second copy of anything, so at its [last use](Docs/articles/rules/last-use.md) a local
or a by-value parameter is a new value. This is what lets `_guard = guard;` and "start now, await later" with a value task work without
a written move.

Only locals and by-value parameters are moved from. A field, an element, a `ref` parameter, a captured variable and a `using` variable can
still be reached by something else after the read, so the last read of them says nothing.

A local that holds a container is moved from only if the method can be seen to own it. Two locals can hold the same task, so the last
use of one says nothing about the other. A local of a non-copyable type needs no such test: every assignment between variables is
already a copy or a move, so each variable owns its value.

The [commit at the end of a `try` block](Docs/articles/rules/commit.md) exists to keep the common shape "acquire, set up, commit, or
dispose on failure" free of warnings.

The last use is the one rule that accepts a by-value read of a stored value without a written move, and a mistake in it would let a
real copy through with no warning. Every other mistake of the analyzer shows as a warning. That is why the MSBuild property
`NonCopyableAllowImplicitMoves` exists: for a project that wants every move written, and as a way out if a defect in the flow analysis
is found. See [MSBuild Properties](Docs/articles/rules/msbuild-properties.md).

#### MovedFrom and What Can Throw

Between the read and the reset of a [`[MovedFrom]`](Docs/articles/rules/moved-from.md) parameter, anything that throws leaves the value in
two places, which the caller can catch. So everything in that window must run no other code. The judgment fails closed: the operations
and operators that run nothing are named one by one, and anything else counts as a call. `^n` shows why: it looks like an operator on a
number, but it creates an `Index`, which throws for a negative number.

Running out of memory is not counted as able to throw: a program does not recover from it. A string concatenation is counted, although
it fails with the same exception: it fails for certain values only, it can be caught, and the program goes on.

A generic move is checked to be a [pure move](Docs/articles/rules/moves-of-containers.md#generic-moves). That is what lets a caller
trust its signature: with a task bound to the type parameter, a slot that the method keeps may hold a task that somebody put there
without owning it.

`[MovedFrom]` is two promises: to the method, that its caller owned what it passed; and to the caller, that the method leaves only what
the caller may own. So it is [kept in both directions](Docs/articles/rules/keeping-marks.md#movedfrom-mark): adding it breaks the first,
dropping it breaks the second.

`[MovedFrom]` on a parameter of a primary constructor gets a warning: the code of a primary constructor is the initializers and the
call of the base constructor, which have no order in which the parameter could be read and then reset.

#### Null References

`Unsafe.NullRef<T>()` makes a null reference in code that the language calls safe, so a read or a write through a reference can fail
and leave a moved value in two places. The rules for [null references](Docs/articles/rules/null-references.md) went through three
versions:

1. At first, a read or a write through a reference counted as able to throw.
2. Then a reference was taken to refer to something. This was taken back after advice from people who write such code.
3. The rule today: a reference counts as able to fail unless it is known to refer to something, by one of three proofs.

The proofs are a call with `[NotNullRef]`, a plain store, and a by-value read whose value is stored. A read into a discard, a read as an
argument and a read as an operand are no proofs. Each of them would need its own account of what the compiler leaves out, and the
accounts differ by reference and by compiler. Measured in an optimized build, the compiler of the .NET 10 SDK emits
nothing for `_ = x;` through a reference to a struct, and that of the .NET 11 SDK emits the read. A store cannot be left out, and
neither can a read whose value is stored, so those two are the proofs: the same for every reference and for both SDKs.

`[NotNullRef]` replaced a helper that the analyzer knew by name, so that a project can write its own check under any name. Its body is
not checked, so the mark is a claim like a symbol list entry. That is why every mark that a project declares gets SCA1047 once: nothing
else in the build shows that someone looked, and the author suppresses the warning after checking the method. SCA1047 has an ID of its
own so that suppressing it leaves SCA1046 standing. The mark counts only on a method that is not virtual, since otherwise it would have
to be followed through every override and implementation, as `[Kept]` is, and a null check needs none of that. It does not count on a
`[Conditional]` method: a proof that is there in one configuration and not in the next is none.

## Generics

#### Marked and Unmarked Type Parameters

A [marked type parameter](Docs/articles/rules/marked-type-parameters.md) is checked exactly, and the caller trusts the mark. In a
referenced assembly the mark is all that the caller's build can see, and it is as good as that library's own build having run the
analyzer. That trust is why a mark from code that was not built with the analyzer is ignored (see
[Code Not Built With the Analyzer](#code-not-built-with-the-analyzer)).

The body of an unmarked generic is never looked at on behalf of its callers, wherever the generic lives: in the same project, another
project of the solution or a package. Its use gets a warning unless the member is on the symbol list. What triggers the warning is the
use of a member, not the naming of a type: a `List<Guard>` field copies nothing.

Delegates are exempt: a delegate type has no code of its own, and its target is checked where the delegate is created.

A struct of a non-copyable type must implement every interface member itself. A member that it leaves to the default implementation of
the interface runs on a boxed copy, also through a constrained type parameter, where nothing shows it.

The marks must be kept by overrides, implementations and delegates. Otherwise an unchecked body could sit behind a marked signature.
See [Keeping Marks on Overrides and Delegates](Docs/articles/rules/keeping-marks.md).

#### Containers Bound to a Marked Type Parameter

The body of a generic treats a value of a marked `T` as one that it alone holds. That is true of a guard: nobody can put the same guard
in twice without a copy that gets a warning. It is not true of a task or a list, which is a reference that the caller may still hold.
So with a container bound to a marked type parameter, [what the generic hands out is stored](Docs/articles/rules/bound-containers.md),
with a pure move as the one exception.

No rule about what goes in could fix this, and two were tried (see
[Alternatives That Were Given Up](#alternatives-that-were-given-up)). Every type that mentions `T` is a way in, and so is every piece of
code that the generic is given: a lambda, a delegate or an object that implements an interface is a new value, and can still push any
stored container in.

A framework collection keeps the ordinary rule, where what a member hands out is as new as what it was called on and with. Its members
are a closed list that was read one by one. The members of a user's generic are not.

## Containers

A [container](Docs/articles/rules/non-copyable-types.md#containers) is a reference type or a view that mentions a non-copyable type.
Copying it is fine, since only a reference is copied. Using it is what the rules control.

#### What a Member on the Symbol List Hands Out

Being on the symbol list means that the member makes no copy other than the value that it hands out. Whether that value is a copy
depends on where it came from, so [what a listed member hands out](Docs/articles/rules/listed-members.md) is as new as what it was called
on and with. Tasks and value tasks get no special case.

The `new` marker is about a value of a non-copyable type, which leaves no second instance behind when it is taken out. A container that
such a member hands out, as from a `Stack<Task<Guard>>`, is a reference that whoever put it in may still hold.

The `owned` marker is accepted only on an instance property of a class whose type is `Task<TResult>`:

- **Not on a struct.** A call on a struct local can replace the whole local (`this = other`), and a copy of the struct shares the task,
  so the task cannot belong to one object alone.
- **Not on a property that hands out a list.** Another holder of the object could use the list's elements in place while the method
  that made it reads them by value.

#### Returning and Keeping

The result of a call is a new value, so a method must not return a container that something else still holds. That is the rule for
[returning a container](Docs/articles/rules/returning-containers.md), with `[return: Borrowed]` for a method that keeps what it
returns. The return of a getter is not checked, since a getter usually hands out what its object holds; callers treat it as stored.

The other side of the same rule: what a method is given, it only uses during the call, unless it says that it
[keeps it](Docs/articles/rules/keeping-containers.md) with `[Kept]`. An iterator runs its whole body after the call has returned, and
an `async` method runs everything after its first `await` then, so each keeps the container parameters that it uses there. The mark is in the signature, so it holds through an
interface, an override and a referenced assembly, where the caller cannot see how the method is written.

The comparison of marks between a method and the member or the delegate that it stands behind (SCA1040) treats a parameter whose
declared type is a type parameter, marked or not, as keeping, as a call through it does: a caller of a `MarkedSink<List<Guard>>`
already counts the list as kept, so a `[Kept]` method behind it adds nothing that the caller could miss. A member that the symbol list
marks `borrows`, and a `[MovedFrom]` parameter, are left to the comparison as before, since a call through them does not take the
container to be kept. A lambda or an anonymous method has no signature that anything calls but the delegate that it is made as, so
the same question decides whether it may keep its own parameter (SCA1032): where the delegate's parameter is taken to keep, it may.
The natural type of a lambda whose container parameter is passed by `ref`, `in` or `ref readonly` is not taken to keep, unlike
`Action<T>`. A caller that passes its own local that way gives it up, but a method that forwards its own parameter that way does not,
and gets no SCA1032, so nothing would warn of a lambda that kept the container behind it. An `out` parameter is given nothing to keep.

A parameter whose type mentions a type parameter that is not marked keeps what it is given, unless the symbol list marks the member
`borrows`. `ArrayPool<T>.Return(T[])` keeps its array and `Array.Reverse<T>(T[])` only uses it, and no declaration tells the two apart.

#### One Description of Each Use

The check of a [local that the method owns](Docs/articles/rules/owned-locals.md) and the check of a parameter that is not `[Kept]` ask
different questions: whether anything else can reach the container or put something stored into it, and whether something still holds
it when the method has returned. They were two lists at first. A round of testing found the misses of the second check exactly where the
first already knew better. So each use of a container is now described once: what is made from it, where that goes, and whether what
receives it keeps. Both checks read that one description. See [What the Keeping Check Follows](Docs/articles/rules/what-keeping-follows.md).

The same thinking applies to the members that an operation binds. A `foreach`, an `await`, a pattern or a collection expression runs
code of the project with the container, where nothing but what the compiler bound shows it. The analyzer reads every symbol that an
allowed operation carries, in one place (`CarriedSymbols.cs`), and a test holds that list against the compiler's API by reflection. A
member that a later compiler adds to one of these operations is then found by the test when the compiler package is raised, and not by
a user.

## Conversions and Boxes

A container that is [converted to a type that no longer mentions](Docs/articles/rules/conversions.md) the non-copyable type gets a
warning, because the code that receives it cannot be checked. There are two IDs:

| ID | Target | What the receiving code can do |
|---|---|---|
| SCA1022 | An untyped collection, such as `IEnumerable` or `Array` | Hand out boxed copies of the elements |
| SCA1023 | An opaque type, such as `object`, `Task` or an interface that is not a collection | Reach the elements through a later cast or reflection |

SCA1023 was expected to be the noisiest warning, since containers reach `object` through logging and assertions all the time, so it
has an ID of its own and can be turned down alone. Tasks, the most common case of SCA1023, are exempt as an [erasable type](Docs/articles/rules/variance-and-erasable-types.md): no base
type or interface of `Task<T>` hands out a value of `T`, so the only way back to the result is a cast, and what that gives is stored.

Binding a container to an unmarked type parameter is not erasure, though the generic code cannot see the elements either. Generic code
can duplicate the reference and hand it back, typed, as the result of a call, which would look new: `tasks.ToArray()[0]`.

A type that derives from an erasable type must stay erasable. A conversion sees only the type that it is written with, and the object
behind it may be of a subclass that hands out more.

[Boxing a new value](Docs/articles/rules/boxes.md) is a move into the box: nobody else holds the value, and inside the box it is one
object that every holder of the reference shares. A generic struct that is non-copyable only through its type arguments is different.
Its box runs unchecked code: `ITuple` and `IEnumerator` hand out a boxed copy of the element at every read. So boxing one is a hiding
conversion. `Nullable<T>` is the exception: its box holds the value itself.

## Attributes and Marks

#### Recognition by Full Name

The attributes are [recognized by their full name](Docs/articles/rules/attribute-recognition.md), in the namespace
`Singulink.CodeAnalysis` of any assembly. Roslyn's rule recognized a class named `NonCopyableAttribute` in any namespace. With six
attributes, some of them ordinary words such as `Kept` and `Borrowed`, that would sooner or later read an attribute of some other tool
as one of these, and a `[Kept]` that means something else turns a warning off.

Stricter matching fails in the dangerous direction for one attribute only. An unrecognized `[Kept]`, `[Borrowed]`, `[MovedFrom]`,
`[NotNullRef]` or `[RequiresCopyable]` means more warnings. An unrecognized `[NonCopyable]` means a struct that is silently copyable in a
build that looks clean. So a `NonCopyableAttribute` in any other namespace gets SCA1045 where it is declared.

On a struct of a referenced assembly, a `NonCopyableAttribute` of any namespace is still recognized. Libraries that are already
published mark their guards with an attribute of their own, most often an internal `Microsoft.CodeAnalysis.NonCopyableAttribute` after
Roslyn's convention, and a project that uses such a library cannot change it. Reading the mark costs at worst a warning that can be seen
and suppressed. Not reading it makes every guard of that library copyable in a build that looks clean. This does not hold for a type
parameter: a mark there says that this analyzer checked the body of the generic, and another tool's attribute cannot say that.

A class that derives from `NonCopyableAttribute` marks like it, since that is a known way to give a mark a name of the domain. A class
that derives from one of the other attributes is not recognized, which means more warnings and not fewer.

An attribute declared `[Conditional]` gets SCA1044: the compiler leaves it out of the assembly, so the marks would hold inside the
library and be gone for everyone who uses it.

## Code Not Built With the Analyzer

#### Why There Is a Marker

The rules trust what a signature says: a marked type parameter, `[MovedFrom]`, `[NotNullRef]`. That holds only for code whose build ran
the analyzer. So an analyzed build carries a marker: an `AssemblyMetadata` attribute that the package's targets file adds to every
project that runs the analyzers, so that the analyzer can tell
[code that was not built with it](Docs/articles/rules/unchecked-code.md) from the rest.

- **The marker is a statement by the build,** like a line of a symbol list file. A project that suppresses warnings still carries it.
- **Its value is the version of the analyzers** that built the assembly, such as `0.7.0` or `0.7.1-local.2`. The analyzers project
  writes the version of the package into the copy of the targets file that it packs, and the attributes project writes its own version
  into its own marker.
- **Any value counts,** so that a later version of the marker is still read as one.
- **An assembly with no marker counts as unchecked,** whatever built it.

#### Granting Marks and Restricting Marks

Marks that grant something, `[NonCopyable]` on a type parameter, `[MovedFrom]` and `[NotNullRef]`, count as absent on a member of
unchecked code. Each promises what only a checked body holds. Marks that only restrict, `[NonCopyable]` on a struct, `[Kept]`,
`[return: Borrowed]` and `[RequiresCopyable]`, are honoured from anywhere: they can only add warnings.

#### What Unchecked Code Is Trusted With

- **A value passed by value is moved,** and is the callee's from then on, as for any callee. Anything else, by reference, in a container,
  in a view or through a pointer, needs a `borrows` entry (SCA1024).
- **A type's own members are trusted with its own values,** the receiver and a parameter of the type itself. `handle.Free()` on a
  struct of a third-party library has to work. A member of the type that takes another non-copyable type is judged like any other.
- **A non-copyable value returned by value is a new value,** since nothing could check otherwise. A container that unchecked code hands
  out is stored unless a `new` entry says otherwise.

#### Unchecked Code Behind a Checked Type

Some routes show at no call: an implementation of an interface of checked code, an override, a delegate that the unchecked
assembly made. A call is judged by the member that it binds to, so code behind it is never
seen. Code can name a type only where its assembly references the assembly that declares the type, so that reference is what gets the
warning (SCA1025), once per assembly, with the `trusted` line of a symbol list file as the answer.

The reference to a declaring assembly says nothing of three kinds of value. The ten built-in non-copyable types are declared by the
framework, which every assembly references. A struct of unchecked code that only a symbol list file makes non-copyable is the same: a
line for a struct of the framework would otherwise give a warning on every assembly of the framework. And a value that is bound to a
marked type parameter is copied by generic code that names no type at all. These are covered from the other side:

- **What unchecked code needs is a way to the value,** and the ways that no call shows all start at a member of checked code. So an
  unchecked assembly that references an assembly of checked code whose members let other code get at such a value gets SCA1025 too,
  with a text that names the member.
- **A member lets a value out in three ways.** It is a field that carries one. It returns a reference to one, or a container or
  another object that carries one. Or it hands one to code that the unchecked assembly wrote: other code can implement the member,
  as a member of an interface, a virtual member or a delegate, or the member calls other code back, through an event or a
  parameter that is a delegate, a function pointer or an interface, and that code is given a reference or a container. A base
  class or an interface that carries a value counts for its type. What a member of checked code returns or takes by value does not
  count: that is a new value, or a move.
- **A delegate and a function pointer are judged by their method,** and not by what their type mentions: `Action<SpinLock>` takes
  the value by value, which is a move, so a field or a parameter of that type lets nothing out, and `Action<List<SpinLock>>` does.
- **A protected member counts only in a class that can be derived from,** and a type that an assembly forwards counts through the
  assembly that it moved to, with that assembly named in the message.
- **The message names the member and no type.** The type of a member is what carries the value, such as `List<GCHandle>`, and not
  the non-copyable type itself.
- **A framework generic that is bound to the value needs no entry here.** `StrongBox<SpinLock>` or `SpinLock[]` reaches unchecked
  code only where checked code hands it over, and that use gets its own warning (SCA1020, SCA1023 or SCA1024).
- **Only the code of checked assemblies is read for this,** and only where an unchecked assembly references one: the values of
  unchecked code are that code's own business, as its types are.
- **A reference assembly does not show what its code uses.** A project reference gives the compiler one: it has no method bodies
  and names only the assemblies that its reachable members use, so an unchecked project of the same solution that uses checked
  code only inside its methods has no reference to it there. The compiler marks such an assembly with
  `ReferenceAssemblyAttribute`, so the analyzer knows when a reference list is one of these. An unchecked assembly that is given
  as a reference assembly gets SCA1025 where a non-copyable value is within its reach: where another referenced assembly declares
  a non-copyable type, lets a value out or names it as a friend, and does not itself reference the unchecked one, which rules out
  that it is used there. The message says which of the three it is. The compilation's own types are never within reach of an
  assembly that it references. The assemblies of the framework are left out by name, as for a base class.
- **That check is the cautious reading, in three ways.** The assembly may use nothing of the other one, and the `trusted` line
  says so. The other assembly can be built on the unchecked one and use it only inside its methods: its own reference assembly
  does not show that, so the unchecked one is reported though it cannot use the other. And a package that comes with a reference
  assembly is judged like a project, though it cannot use a project of the solution. Making every project compile against
  implementation assemblies (`CompileUsingReferenceAssemblies`) would show the real references, and would slow every incremental
  build.
- **Internal members are not imported from a referenced assembly,** so what they let out cannot be seen. An unchecked assembly that
  checked code names in an `InternalsVisibleTo` attribute can use them, and gets the warning whatever that code holds. This also
  covers an internal `[NonCopyable]` struct, which the check of the declaring assemblies cannot see either.

## Symbol List

#### Closed and Read One by One

The built-in framework symbol list names members exactly, per overload, with no entry for "every member of a type" and no wildcard. Each
entry was read in the runtime's source, and the evidence is in
[NonCopyableSymbols.evidence.md](Source/Singulink.CodeAnalysis.Analyzers/NonCopyableSymbols.evidence.md), beside the list. The criterion is
the one of the principles: a member is on the list only if it makes no copy other than the value that it hands out. Members of
`Unsafe`, `MemoryMarshal` and the other marshal classes are on the list for that one fact. The analyzer guarantees nothing about code
that uses them to do what the language otherwise rules out (see "Members That Make Read-only Storage Writable" in the evidence file).

The scope of the list was chosen by who uses non-copyable structs. They are a low-level tool: in nearly all code a guard is a local or a
field, and sometimes an array element reached through a span. So the list covers thoroughly what such code uses (spans, memory, arrays,
the marshal classes, pools, `Interlocked`, everything that `await` calls), and the general-purpose collections get their basic members.
A gap there costs a warning that a project can suppress, or answer with a line of its own symbol list file.

`FrameworkScanTests` keeps one part of the list complete. A member of the framework that takes one of the ten built-in types other than
by value needs a `borrows` entry, and one that hands out a container of one needs `new` (see
[Code Not Built With the Analyzer](#code-not-built-with-the-analyzer)). The test loads every assembly of the shared frameworks that the
tests run on, finds every such member, and fails where one has no entry with the marker that it needs. Since it reads the runtime that
the tests run on, each test target scans its own version of .NET.

#### Merging

Where two entries disagree, the one that claims less applies. So a symbol list file can add entries and tighten a built-in one, but cannot
loosen it. The `copyable` marker is one exception, against the built-in framework symbol list: taking a type out is what the marker is
for. `clones=complete` is the other: it counts wherever a line says it, against a plain line of a file or of the built-in list (see
[Classes That Clone Themselves](#classes-that-clone-themselves)).

An entry that names something the project does not reference is ignored, so that one file can serve several projects. A `noncopyable`
line that names nothing gets a warning all the same: a mistyped ID there would leave a type copyable in a build that looks clean.

There is no MSBuild property to mark a type or take one out. The symbol list is the one place for both.

#### Built-in Types Through a Library

The ten built-in non-copyable types are on in a project that references one of the two packages, and off in a project that gets the
analyzers only through a library. A library that marks its guards brings the analyzers to everyone who uses it, so that they cannot copy
those guards. It must not bring warnings about their own `GCHandle` code with it. See
[Leaving out the built-in non-copyable types](Docs/articles/guides/built-in-types.md).

The default is set by the package's targets file, and not by a props file, because the project's items are not known when props files
are read. The analyzer takes only `true` for on, so that a build in which the targets file did not run has the ten off.

#### Classes That Clone Themselves

A base class of another assembly can clone itself with `MemberwiseClone`, as `Encoding`, `CultureInfo` and `Calendar` do, and the
clone copies every field of a class that derives from it. The analyzer cannot read the bodies of referenced assemblies, so it cannot
see the call. A warning on every class that holds a non-copyable value and has a base class of another assembly would hit every class
that derives from a framework class: every stream, form or control that holds a pinned buffer. So a `clones` line names the classes
that do clone themselves, and a class that derives from one of them gets the warning (SCA1009).

That leaves the base classes of which nothing is known. They get the warning too, with a text of its own:

- **A base class is known in three cases.** Its assembly is the compilation or was built with the analyzers, where a call of
  `MemberwiseClone` gets its own warning. Or its assembly is part of the framework, of which the built-in list says which classes
  clone themselves. Every other base class may clone itself, and a field behind it gets the warning until a line says what it does.
- **The framework is the assemblies that the scan read,** by their simple names: `FrameworkAssemblies.txt` lists the assemblies of the
  shared frameworks `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App` and `Microsoft.WindowsDesktop.App` of .NET 10 and .NET 11, and
  `FrameworkScanTests` fails for an assembly of a shared framework that the file does not name. The assemblies that forward to them
  for another target, such as `netstandard`, `mscorlib` and `System`, are among them, so a project for .NET Standard or the
  .NET Framework is judged by the same names. A library of Microsoft's that is no part of a shared framework, such as WinUI, was not
  read, so it is another library. So is an assembly that only the .NET Framework has, such as `System.Messaging`, or that only a
  platform has, such as `Mono.Android`: 82 of the 133 reference assemblies of .NET Framework 4.8 are not in the file. The name was
  chosen over the publisher on purpose: the list says what was checked, not who wrote the code.
- **A `clones=none` line counts only for a class that is not known.** For a class of checked code or of the framework it has no
  effect, gets SCA1042, and does not end the walk, so it cannot vouch for an unknown class above it by the way.
- **Three lines answer it.** `clones` says that the class clones itself. `clones=none` says that neither the class nor a class above it
  does, so one line for the direct base class is enough. A `trusted` line for the assembly says that no code of it copies a
  non-copyable value, and the walk goes on above the classes of that assembly.
- **`clones=none` is a value of its own, and not an empty `clones=complete`.** A class can be cloned through a member of another
  class, as a static `Cloner.Copy(Document)` that calls an internal member does. Its correct lines are `T:Document;clones=complete` and
  `M:Cloner.Copy(Document);clones`, with no member line for `Document` itself, so "complete, and no member line" cannot mean that
  the class never clones.
- **`clones` wins.** A class that a `clones` line names, itself or above it, is looked for first, and a `clones=none` line for such a
  class has no effect and gets SCA1042.
- **Only the field check changed.** A generic class of such an assembly was covered before: binding a non-copyable type to it gets
  SCA1020, and each use of a member on such an object gets SCA1023 until a line vouches for the member.

SCA1009 has an ID of its own, apart from SCA1005 for a call of `MemberwiseClone` in the compilation: turning down or suppressing one
leaves the other. Its descriptors are the field, the binding, and the uses below.

- **One line per class that makes the call.** The check walks the base classes, so a line covers every class that derives from the
  class it names, and the generic classes that derive from it. The framework's own derived classes, every `Encoding` and every
  `Calendar`, need no line for the warning on a field, and the built-in framework symbol list has 11 class lines that clone where one
  per derived class would have about 100.
- **The list is not picked by hand.** `FrameworkScanTests` reads the IL of the shared frameworks of both targets, finds every call of
  `MemberwiseClone` on `this`, or on the argument of a static member of the class, in a class that another assembly can derive from,
  and fails for a class with no line of its own or of a base class, and for a public or protected method that clones and has no line
  of its own. `Delegate` and `MarshalByRefObject` are the two exceptions, with their reasons in the test.
- **`clones` only adds warnings,** so it is honoured from every symbol list file and whatever built the class's assembly, and it is kept
  apart from the other markers: nothing takes it away. A library that keeps a class open and cloneable ships the line, and SCA1008 says
  so. A line that says a member which a `clones` line names makes no copy is left out, with SCA1042, whichever file has the `clones`
  line.
- **`clones=complete` counts wherever a line says it.** A class has complete member lines where any `clones` line for it, of the
  built-in list or of any symbol list file, says `clones=complete`. A plain `clones` line says that the class clones itself and nothing
  about completeness, so it takes nothing away, and nothing else does: the answer to lines that miss a member is a `clones` line for
  that member, which any file can add. A file may say it for a class that the built-in list names plain, such as `XmlSchemaObject`, and
  so vouch for members that the built-in list did not analyze. It is a value of the marker and not a second marker, since it says
  something only of a `clones` line of a class.

**Where the check stands: the use, not the binding.** A binding can be written, inferred or received in more places than a list of
places could hold: `new`, a `dynamic` creation, `new T()`, `Activator`, the return of a factory, a type argument. The argument for
completeness goes by what the clone needs instead. An object of a class whose base chain reaches a `clones` class is cloned only by
code that the analyzer does not read, the cloning class's or that of a class above it, or by a call of `MemberwiseClone` in checked
code, which is SCA1005. The object reaches that code in two ways:

1. **A member of that code is used on it.** The call is followed to the member that it runs, an override included, and the member gets
   the warning where it can clone the object or hand it to code that can. A member with a `clones` line does one or the other. A member
   whose signature shows that it can hand the object on as a type that hides the binding gets the warning unless a line of its own
   says that it does not: a result, or an `out` or `ref` parameter, of such a type or of a type that contains one (a type argument, an
   array element, a tuple element, a delegate's return); a parameter of a delegate type that takes one, a callback; and an event whose
   handler type takes one, where it is subscribed to. A member that no line names, of a class whose lines are not complete, counts as
   one that can clone. A line for a member does not vouch for an override of it: a member declared in the class that shows the binding, or in a
   class between it and the class that clones itself, is judged by its own line and its own signature. What a signature does not show,
   such as a member that stores its object in another object that it returns, or gives it to a visitor interface, a registry or a
   wrapper class, is the list's: for a class whose line says `clones=complete`, a member with no line is taken neither to clone the
   object nor to hand it on.
2. **It is given to a member as an argument of a type that hides it,** or converted to such a type in any other way, from which any code
   can reach a member that clones: the class that clones itself, a class above it, `object`, `dynamic`, an interface, or a type
   parameter that is not marked.

So the hiding checks have to see every conversion that C# applies, also those that are no conversion node of the operation tree. The
list of those was made from Roslyn's own API, not from the cases that were found: every public member of Roslyn 5 that carries a
`CommonConversion` or a C# `Conversion`, and the places of the language that convert with nothing written. The checks that read a
conversion are the hiding check of a container (SCA1023), the check of an object with a binding (SCA1009), and the checks of a
non-copyable value that a conversion boxes or copies (SCA1004 and the copy rules).

| Where C# converts | Can it carry what the checks look for? | How it is covered | Test |
|---|---|---|---|
| `IConversionOperation.Conversion`, a node | Yes | Visited: `VisitConversion` | `ConversionTests`, `CloningUseTests` |
| `IArgumentOperation.InConversion` and `OutConversion` | No: C# puts the conversion of an argument in its value, as a node, and an argument by reference has the type of its parameter. Roslyn fills them for Visual Basic | Nothing to see | `ImplicitConversionTests.ArgumentConversions_AreIdentitiesInCSharp` |
| `ICoalesceOperation.ValueConversion`, the left side of `??` | Yes | Visited: `VisitCoalesce`, the hiding check and the boxing of a stored value (both missed before) | `Coalesce_ConvertsItsValueAsAConversionWould` |
| `ICompoundAssignmentOperation.InConversion` and `OutConversion` | Yes, with a user-defined operator whose left parameter is a base type or `object`, or whose result converts back to the target. A user-defined conversion is a member that is used | Visited: `VisitCompoundAssignment` (missed before) | `CompoundAssignment_ConvertsTheTargetAndTheResultAsConversionsWould` |
| `ISpreadOperation.ElementConversion` | Yes | Visited: `VisitSpread`. A stored non-copyable element is a copy, SCA1030 on the spread | `Spread_ConvertsEachElementAsAConversionWould` |
| `DeconstructionInfo.Conversion` and `Nested`, of an assignment, a declaration and a `foreach` | Yes, for an element of a tuple variable, of a call's result and of an output of `Deconstruct`. An element of a tuple literal is a node | Visited: `CheckDeconstruction`, the hiding check and the boxing of a stored element (both missed before) | `Deconstruction_ConvertsEachElementAsAConversionWould` |
| `DeconstructionInfo.Method`, the receiver of `Deconstruct` | Yes, for an extension on a base type, with no argument in the tree; an instance member is a receiver | Visited: the receiver check and the receiver of an extension that a pattern calls (missed before) | `PatternCall_ConvertsTheReceiverOfAnExtensionAsAnArgumentWould` |
| `ForEachStatementInfo.ElementConversion` | Yes | Visited: `AnalyzeForEach` | `ConversionTests.ForEach_ConvertsEachElementToTheTypeOfItsVariable` |
| `ForEachStatementInfo.CurrentConversion` | No: the element type of a `foreach` is the type of `Current`, so the conversion changes at most the names of tuple elements or nullability | Nothing to see | None |
| The members that a pattern calls: GetEnumerator, MoveNext, Current and Dispose of a `foreach`, GetAwaiter and GetResult of an `await`, Dispose of a `using` | Yes, as receivers, and for an extension as a conversion of the receiver | Visited: `FollowMember` and `CheckDisposal`, with the extension's receiver (missed before) | `PatternCall_ConvertsTheReceiverOfAnExtensionAsAnArgumentWould`, `CompilerCopyTests.ForEach_RunsGetEnumeratorOnACopyWhereAWrittenCallWould` |
| Patterns: the input converted to the narrowed type, and the members of a positional, property or list pattern | Yes | Visited: `Patterns.cs` | `PatternCall_ConvertsTheReceiverOfAnExtensionAsAnArgumentWould`, `CloningUseTests.Use_InEveryFormThatHidesTheTypeGetsAWarning` |
| `lock`: the value given to `Monitor.Enter` | Yes | Visited: `VisitLock`, let through because the list marks `Monitor.Enter` `borrows` | `ReceivingMemberTests.BorrowingMember_LetsTheValueThrough` |
| Tuple comparison | The conversion of an element is a node; a comparison of references hands it to no code | Visited, and let through as `a == b` is (a warning there was wrong before) | `TupleComparison_OfReferencesHandsTheElementsToNoCode` |
| Interpolated strings: a hole, and the AppendFormatted of a handler | Yes | Visited: a node, or a type argument of the member that is used | `LanguageConstructTests.InterpolatedStringHandler_FormatsAContainerAsAPlainStringDoes` |
| Collection expressions and initializers: an element, the Add of an initializer, a builder | Yes | Visited: nodes, and the argument of the member that receives them | `ReceivingMemberTests.ListedMember_ThatKeepsWhatItIsGivenGetsTheWarningOfTheConversion` |
| Query clauses | Yes | Visited: invocations with conversion nodes, Cast for a range variable with a type | `CompilerCopyTests.Query_CallsItsMethodsOnACopyOfItsSource` |
| Method groups: the parameters and the result of the method | Yes | Visited: `CheckMethodGroupConversion`. A method group of a member is a use of the member, so one of a member that clones gets the warning of a call of it | `ImplicitConversionTests.MethodGroup_IsJudgedAsAUseOfItsMemberAndItsTargetIsNotChecked` |
| The `Target` of a delegate made from a method group, which holds what the group is made on as an `object` | Yes, for code that reads `Delegate.Target` | Not checked, as reflection is not: see [What Is Not Covered](Docs/articles/rules/not-covered.md) | `ImplicitConversionTests.MethodGroup_IsJudgedAsAUseOfItsMemberAndItsTargetIsNotChecked` |
| The receiver of an instance member | Yes | Visited: `CheckReceiverType`, and `CheckMemberOfClonedObject` for the first way above | `CloningUseTests.Member_OfTheCloningClassOnTheObjectGetsAWarningOnlyWhereItCanCloneIt`, `CloningUseTests.Member_ThatCanHandTheObjectOnAsItsSignatureShowsGetsAWarning` |
| The code that the compiler writes for a record or an anonymous type, which calls `ToString`, `Equals` and `GetHashCode` of its members | It matters only where one of those three is a member that clones | Not checked. The code does nothing else with the values of the members, and no `clones` line of the built-in list names one of those three. What remains is a class of another library whose own `ToString`, `Equals` or `GetHashCode` clones its object | `FrameworkScanTests.CompilerWrittenMembers_OfRecordsAndAnonymousTypesCallOnlyToStringEqualsAndGetHashCodeOfTheirMembers`, `FrameworkScanTests.BuiltInList_NamesNoToStringEqualsOrGetHashCodeAsAMemberThatClones` |
| A member of a base class that runs an override of a class below it, where the override clones the object: `CultureInfo.GetFormat` returns `NumberFormat`, which a class of a library overrides with a getter that clones the culture | Yes | Not checked. A line for a member says what that member's own code does, and the scan of the framework reaches the framework's own overrides only. A use of the override itself gets its warning by its `clones` line. The fix would be to look at the overrides below the member's class | `CloningUseTests.Member_OfABaseClassThatRunsAnOverrideThatClonesGetsNoWarning` |
| The operand of a user-defined conversion or operator, converted to its parameter | Yes | Visited: a member that is used, with its parameter | `CompilerCopyTests.Operators_UserDefinedAndAlsoAndOrElsePassTheLeftOperandTwice` |
| The return of an override with a covariant return type | Yes | A check of the declaration | `ConversionTests.CovariantReturn_IsTheConversionThatItStandsFor` |
| `SemanticModel.GetConversion`, `ClassifyConversion`, `GetSpeculativeConversion` | They read the conversions above | Nothing more to see | None |
| A `dynamic` operation, `__makeref` and a pointer | The binder converts at run time; a typed reference and a pointer are not followed | Not covered: see [What Is Not Covered](Docs/articles/rules/not-covered.md) | None |

So the binding's object is made one that those checks report:

- **Which objects.** `FindUnjudgedCloningBinding` finds, for a class, a binding of a non-copyable type to a type parameter of a class in
  its chain up to the cloning class, by the type arguments for a class of another assembly and by the fields that mention a type
  parameter for one of the compilation. It counts the binding where no declaration of the compilation judged it: the class is
  constructed with it, as `Tagged<Guard>` or `Tagged<T>` in a generic method, or the base list that binds it is in another assembly, or
  the declaration of the class of the compilation that names the bound class binds nothing that it shows, as `DU<T> : Tagged<T>` with an
  unmarked `T` does. Such an object is not erasable, whatever its marks or a list line say, and its conversion to an interface hides it
  too, since the interface can be converted on to ICloneable with nothing left that shows the binding.
- **What it gets.** The use gets SCA1009 in place of SCA1023, with a text for a conversion, for a member, for a member that clones, for
  a constraint and for a type parameter that is not marked, so that one reason gets one ID.
- **The member that receives it.** A line of the symbol list with no marker says how a member treats the values that it holds, not
  that it forgets a reference that it is given: `List<object>.Add`, `Task.FromResult`, `SetResult`, `TryWrite` and
  `Interlocked.Exchange` keep it. So an argument converted to a type that hides it is let through only where the list marks the member
  `borrows`, for a container as well as for such an object. 32 members of the built-in list that keep nothing of what they are given
  got `borrows` with this: the comparisons of `Unsafe`, `Array.Clear`, `ReferenceEquals`, `ThrowIfNull`, `GC.KeepAlive`,
  `RuntimeHelpers.GetHashCode`, `Task.WaitAll` and `WaitAny`, and the 15 members of `Monitor`. The key lookups of the dictionaries do
  not borrow: they give the key to the dictionary's comparer, code that the application can supply, which can keep it.
- **One warning where one is enough.** A class of the compilation whose binding is in its own base list, or in that of one of its base
  classes, got the warning there, and a class of the compilation with a field of a non-copyable type got it on the field. Their objects
  get none for the clone where they are used, and 'this' and 'base' in their own code are judged as the class. Where such a base list
  binds to an unmarked type parameter of a generic of the project, a conversion of their objects to the base class keeps SCA1023, for
  the code of that generic, which is another reason.
- **The early warnings.** The base list and the object creation, a `dynamic` one included, still get the binding warning in the project
  that writes the binding. Completeness does not rest on them. An object that is converted where it is created gets only the
  conversion's warning.
- **The generic's own project.** The field of a marked type parameter gets SCA1009 there, since a clone copies what is bound to it, and
  its text says to suppress it only where the class's own code never clones its objects. Inside the generic class, `this` is judged by
  that field. Elsewhere in the generic's project, `new Tagged<T>()` in a factory is constructed with a binding, and its conversion to
  `CultureInfo` or an interface gets the use warning there. A field of an unmarked type parameter gets no warning of its own, and every
  member of the generic gets SCA1020 for the project that binds a non-copyable type to it.
- **What is left.** What the code of another assembly does with such an object inside itself is judged in that assembly's own build,
  as everything else that a checked assembly does. Reflection, `dynamic`, `__makeref` and pointers step outside the language's safety:
  see [What Is Not Covered](Docs/articles/rules/not-covered.md).

**What a member with no line rests on.** The first way rests on the `clones` lines of a class naming every member that clones its
object or hands it to code that can, where the class's line says `clones=complete`: a member that no line names is then taken to do
neither, and that is the list author's statement. Without it, each member is judged by its own line, and one that no line names counts
as one that can clone, so a list where someone wrote the type line, or the lines of a few members, stays safe. What backs the
statement:

- **The built-in list.** `FrameworkScanTests` follows the calls of the framework, in its IL, from the members of every class whose chain
  reaches a `clones` class to the members that clone an argument: `this` or a parameter, with MemberwiseClone, or by giving it to an
  argument of a member that clones it, a virtual call reaching the overrides of its class's family, and what a member returns followed
  for the members of those families. It fails for a public or protected member that clones and has no line. It found, besides `Clone`
  and the two `ReadOnly`, `Stroke.GetClipResult` and `Stroke.GetEraseResult`, which clone the stroke, and `Stroke.Draw` and `DrawCore`
  with drawing attributes, which clone the attributes that they are given. The members of the Xml classes were not analyzed, so
  XmlSchemaObject and XmlQualifiedName are not marked `clones=complete`, and every member of them and of the XmlSchema classes counts
  as one that can clone. GiveFeedbackEventArgs clones through an internal member only, has no member line, and is marked
  `clones=complete`. Five members whose result the object converts to have a line with no marker, `CultureInfo.Parent`, `GetFormat` and
  `GetConsoleFallbackUICulture`, and `GetPropertyData` of `Stroke` and `DrawingAttributes`, and the scan checks that no path of them
  returns the object. That is a statement of the listed member, not of an override of it, which is judged on its own.
- **The built-in list: what reaches other code.** A second pass of the same scan follows where the object that a member is called on
  can go, for the classes whose line says `clones=complete` and the classes that derive from them, to a fixed point: stored in a field, a static or an element, given to a method with no
  body to read (a delegate's `Invoke`, the constructor of a delegate that captures it), or given, other than as the object that the call
  is made on, to a virtual method that an application can override. Each member that it flags has a `clones` line or an entry in
  the section "Members Whose Own Object Reaches Other Code" of NonCopyableSymbols.evidence.md that says why it hands nothing on, and the test fails for one with neither. Constructors
  are followed too, for an object that one stores in another object that a member hands out: the constructor of `PrinterSettings` stores
  the object in the `PageSettings` of `DefaultPageSettings`, whose `PrinterSettings` hands it back, so `DefaultPageSettings` has a
  `clones` line. The members of `Stroke` and `DrawingAttributes` that raise their events need no line: a handler gets the object only
  where it was subscribed through it, which warns by its signature, or through a type that it was converted to, which warns there.
  Two things in that pass are a judgement written into the scan, not a proof. The extension points of a family count as its own code:
  the encoder and decoder fallbacks and their buffers, `Encoder` and `Decoder`, the buffers that an encoding makes for one call, the
  `GregorianCalendarHelper` of the calendars, and the `StrokeNodeIterator` and `StrokeInfo` of a stroke; an application's own subclass
  of a fallback, an `Encoder` or a `Decoder` does receive the object. And the methods of `Monitor` and the identity hash code, which have
  no body to read, are taken to keep nothing.
- **Signatures.** Whatever a member's signature shows is checked by the first way, whatever the list says of the member's class, for a
  member of the bound class, of a class between, and an override. One walk over the types of the signature decides it, by polarity and
  variance. A result, a `ref` or `out` parameter, and what can be read start at "out"; a parameter, what can be set, and an event's
  handler start at "in"; a `ref` parameter is both. A type argument, of the type or of a type that contains it, goes by its type
  parameter: `out` keeps the polarity (`IEnumerable<Doc>`), `in` turns it round (`IComparer<Doc>`, `IProgress<Doc>`), and no variance,
  as in a class, a struct or an invariant interface (`List<Doc>`, `Task<Doc>`, an enumerator), goes both ways, since the type can take a
  value in and hand one out. An array's element and a pointer's target go both ways; tuple elements and `Nullable<T>` keep the polarity,
  since a value passed by value comes back to nobody; a type parameter's constraints keep it. A delegate or a function pointer keeps it
  for its result and turns it round for its parameters, its `ref` and `out` parameters counting both ways. The member can hand its
  object on exactly where a hiding type stands at "out": so `Action<Doc>`, `IComparer<Doc>` and `List<Doc>` parameters and an
  `IEnumerable<Doc>` result can, and `Action<Action<Doc>>` and `IEnumerable<Doc>` parameters and an `IComparer<Doc>` result cannot.
  `dynamic` and `object` are hiding types at "out" only. An interface or class whose members take the object without a type argument
  that shows it, such as a visitor interface, stays a decided limit: the list says what such a member does.
- **The classes that the lines cover.** A member of a class between the object's own class and the class that clones itself is known
  where a line for that class itself says `clones=complete`, or where the line of the class that clones itself says it and the class
  between is of the same assembly or of an assembly that was built with the analyzers. A plain `clones` line for the class between
  changes neither. The same holds for a class of an assembly that was not built with the analyzers that writes the binding in its own
  base list, as `Bound : Tagged<GCHandle>` does: it shows the binding, but no build judged its members. A generic class of such an
  assembly whose binding comes from its own type arguments alone gets SCA1020 for each of its members instead; where a base list above
  it also binds a closed non-copyable type, as `Both<T> : Tagged<GCHandle>` does, its members are judged for that binding as well, and
  the messages of a member name the closed binding first. The
  framework's own derived classes have lines of their own: the 19 public classes that derive from a class whose line says
  `clones=complete`. FrameworkScanTests fails where those lines differ from the classes that the scan finds, and where a line says
  `clones=complete` for a class that the scan does not cover; SafeListTests fails where they differ from the classes of the reference
  packs of .NET 10 and .NET 11, where `UTF8Encoding` is in another assembly than `Encoding`. Any other class between, such as one of a
  library that was not built with the analyzers and that derives from a framework class, keeps every member as one that can clone,
  with a message that names the line that would cover it.
- **The class that clones itself is the topmost one.** A chain can have a `clones` line below the class that clones itself, such as a
  `clones=complete` line for a class between. The checks take the topmost class with a line as the class that clones itself: the
  chain up to it is where a binding is copied, so a binding above a class between is still found, and a line below speaks only for the
  members of its own class.
- **Checked code.** In a library, a member of a class whose chain reaches a `clones` class, of a class that another project can derive
  from or bind a type to, gets SCA1008 where it clones its own object without a `clones` line of its own: with MemberwiseClone, through
  a member that clones, or through an interface that the class implements with one, as `((ICloneable)this).Clone()`. Only a member that
  another assembly can name gets it, since a line for a private or internal member has no effect where the library is referenced. It is
  a help to the library's author in completing the lines before the type line says `clones=complete`. Its message gives that line:
  the line of the member's class where the library has a `clones` line for it, or else that of the class that clones itself where
  that class is the library's own. Either takes effect in a project that uses the library. It is not a proof: a member that clones its object through a private helper or a
  static helper, or hands it to other code, gets nothing, and the lines of the library are its author's statement. An application gets no such
  warning, since no other project goes by its lines.
- **The lines of a library that is not analyzed** are the user's statement, as every line is.

- **The members that clone have lines too.** A `clones` line may name a method, a property or an event, and the built-in list names
  the public and protected members of the classes that clone themselves that clone. A line for an event counts where a handler is added
  and where one is removed. A line of any marker that names an accessor, `get_Title` or `add_Changed`, has no effect, since every use
  of an accessor is looked up under its property or event, and gets SCA1042 with the line to write instead. A use of one gets a text that says no line can vouch for it, and a line that
  says such a member makes no copy gets SCA1042 and is left out.
- **A warning for each route.** A field can be copied by a call of `MemberwiseClone` in the compilation and by the clone of a base
  class that clones itself. They are two routes: writing the copy out answers the call, and not the base class's clone. So the call
  gets SCA1005 and the field gets SCA1009, and suppressing the one leaves the other.

## Alternatives That Were Given Up

| Tried or considered | Why it was given up |
|---|---|
| Recognizing `NonCopyableAttribute` in any namespace, as Roslyn's rule does | Other tools' attributes named like these would turn warnings off; see [Attributes and Marks](#attributes-and-marks) |
| Roslyn's exemption for awaiting a stored `ValueTask<T>` | Every read of the result of a stored task is a copy, the first one included. "Start now, await later" is covered by the last use and by `NonCopyable.Move` |
| Roslyn's warning about wrapping a non-copyable type in `Nullable<T>` | `Guard?` and tuples are generic structs over a non-copyable type like any other, and the general rules cover them |
| For containers bound to a marked type parameter: first a rule on what goes into the generic, then judging what comes out by what went in | Every type that mentions `T` is a way in, and every lambda, delegate or object that the generic is given can push a stored container in, through an `Action<Builder<T>>`, an `out T` callback or a getter. Replaced by "what the generic hands out is stored" |
| Lists of what ends a local being the method's own | They always missed a way: a marked collection that keeps the list, a helper that hands its argument back, a `params` parameter, a view of the local that is stored. Replaced by a list of what is allowed |
| A list of the ways of keeping a container parameter | Each way that was not named was allowed: a `params` collection, an operator of a marked generic, an extension receiver, a method group. Replaced by a list of what is allowed |
| Using the list of an owned local for the keeping check too | Measured on real code, it gave a warning on `ArgumentNullException.ThrowIfNull(list)`, `ref Guard r = ref span[1];` and `list.Add(guard)` as keeping. The two checks got lists of their own, and later one shared description of each use |
| Collecting the members that an operation binds by hand | Each round of testing found more. Replaced by reading every symbol that the operation carries, held against the compiler's API by a test |
| Describing what an operation hands back for the kinds that were thought of, and "nothing" for the rest | Missed an operator that returns a `Memory<T>` of its operand, and a member that returns a reference that it was given. Now every kind is described, and a kind that is not is not followed |
| A simpler rule for patterns: nothing else may touch a part that a member runs on | Gave a warning on every list pattern over a struct whose length and indexer are not `readonly` |
| Taking each pattern to call a member again | Wrong: the compiler evaluates a member once for a whole pattern or `switch` and shares the result |
| Deconstructing a local in place | Wrong: the compiler copies the right side of a deconstruction into a temporary first, a local included |
| Taking the temporary of an `in` argument that is not a variable for harmless | The callee holds a second instance of a stored guard for the length of the call, and a callee is not a `readonly` member. It gets a warning |
| Following the build configuration for the copy that a `switch` makes | A warning that comes and goes with the configuration would be worse than one that is strict |
| Taking a reference to refer to something | Taken back after advice from people who write such code; see [Null References](#null-references) |
| Counting more reads as proofs that a reference refers to something | Each needed its own account of what the compiler leaves out, different by reference and by compiler |
| A null-check helper that the analyzer knows by name | Replaced by `[NotNullRef]`, so that one rule holds for every reference and a project can write its own check |
| Counting a string concatenation as running nothing | It can fail for long strings, and that failure can be caught |
| Walking from a lambda's graph into the graph of the method around it | The method's `try` is not around the lambda's body. It also made the analyzer fail with an exception on a lambda inside a `foreach`, which left the rest of the member unchecked |
| Taking the members that make read-only storage writable off the list | The list says of a member only that it makes no copy. "Members That Make Read-only Storage Writable" in the evidence file has the reasoning |
| A `borrows` entry for the explicit conversion of `Nullable<T>` | It would also let a stored nullable of a non-copyable type be passed by value |
| Reporting every class that holds a non-copyable value and derives from a base class of unchecked code | It would hit every stream or form that holds a pooled buffer. A `clones` line names the classes that clone themselves instead; see [Classes That Clone Themselves](#classes-that-clone-themselves) |
| The built-in types on in every project | A library would bring warnings about its users' own `GCHandle` code |
| Setting the default of the built-in types in a props file | The project's items are not known when props files are read |
| An MSBuild property to mark a type or take one out | The symbol list is the one place for both |

## Warnings on Correct Code That Were Accepted

The analyzer follows a value through one method at a time and assumes that a value passed by value is gone. Some correct code still
gets a warning. These were accepted:

| Shape | Why it stays | Where it is told |
|---|---|---|
| An `async` helper that fills a container which its caller awaits | The helper keeps running after its first `await`, and nothing shows that the caller waits for it | [Correct Code That Still Gets a Warning](Docs/articles/guides/correct-code-with-warnings.md) |
| Handing a value over and keeping it on failure (`TryWrite`, `TrySetResult`) | What the analyzer sees is a value passed by value and used again | The same |
| A race between two tasks that hold a guard | The same | The same |
| `ManualResetValueTaskSourceCore<T>.GetResult` in a value task source | `GetResult` leaves the result in the core until `Reset`, so the warning is accurate; it is suppressed there | [Value Task Sources](Docs/articles/rules/listed-members.md#value-task-sources) |
| A lambda of an expression tree that keeps its parameter (SCA1032) | The lambda is not made into a delegate where it stands, so nothing says what its callers take, and it is judged as a method is. A tree that is compiled and run that way is rare | [Lambdas](Docs/articles/rules/keeping-containers.md#lambdas) |
| A `switch` statement over a stored local, in an optimized build | The compiler copies the local only in a build that is not optimized, or with a `when`; the analyzer does not follow the configuration | [Read-only References and Temporary Copies](Docs/articles/rules/read-only-references.md) |
| An `in` argument that is not a variable, over a stored value | The callee holds a second instance for the call | [Read-only References and Temporary Copies](Docs/articles/rules/read-only-references.md) |
| A union written by hand with `[Union]`, matched through `TryGetValue` | Judged as a box like every union: strict, and safe, since such a union is rare and its `Value` boxes a copy anyway | [Boxes and Unions](Docs/articles/rules/boxes.md) |
| `map.Keys.ToList()` on a dictionary of guards (SCA1023) | `erasable` cannot say "for `TValue` only". The clean form is `[.. map.Keys]` | [Dictionary Keys](Docs/articles/rules/framework-symbol-list.md#dictionary-keys) |
| `[NotNullRef]` on every method that a project declares (SCA1047) | The claim is not checked, and nothing else shows that someone looked | [Null References](Docs/articles/rules/null-references.md) |
| `MemberwiseClone` in a class that another assembly can derive from (SCA1008) | A derived class there can hold a non-copyable value that the call cannot see. It has its own ID so that turning it down keeps SCA1005 | [SCA1008](Docs/articles/diagnostics/sca1008.md) |
| `await foreach` over a type whose `MoveNextAsync` returns something other than a framework task | The compiler that the analyzer is built against does not give the members of that awaiting, so the loop is a use that is not known to be safe | Not in the Rules articles; see `AnalyzeForEach` in `DoNotCopyValueAnalyzer.Members.cs` |
| New syntax of a compiler newer than the tested ones | Gets a warning as a use that is not known to be safe, until that compiler has been tested | [What Is Not Covered](Docs/articles/rules/not-covered.md#compilers) |

## Limits That Were Accepted

[What Is Not Covered](Docs/articles/rules/not-covered.md) lists the gaps that a project can meet. Where a reason was recorded for
leaving one open, it is here.

- **A class of a framework newer than the ones that the built-in framework symbol list was checked against.** The analyzer cannot
  read the bodies of referenced assemblies; see [Classes That Clone Themselves](#classes-that-clone-themselves).
- **The members that the compiler writes for a record or an anonymous type.** Their `ToString`, `Equals` and `GetHashCode` call the
  same member of each of their members, and those calls are not checked. It is left off the page: it takes a class of a library
  whose own `ToString`, `Equals` or `GetHashCode` clones its object, and the framework has none, which two tests check.
- **What the unsafe classes allow.** The symbol list says of a member only that it makes no copy. The two rules that ask what can throw
  are the exception: they follow a null reference.
- **A handle rebuilt from its value.** The analyzer cannot follow an `IntPtr`.
- **Interceptors.** They replace the method that a call binds to after the analyzer has looked at the call.
- **Code that runs where the language shows none,** such as a remoting proxy of .NET Framework that answers a type test with code of its
  own. The rules that ask what can throw take a type test for a class to run nothing.

## Deferred and Open

These were raised and not done:

- **Listing `ConcurrentDictionary<,>.GetOrAdd(TKey, Func<TKey, TValue>)` for a better message.** No use of it with a guard is clean,
  so it is left off, and its use gets the general warning for a member that is not on the symbol list.
- **Entries that would need two markers.** `BlockingCollection<T>.AddToAny` and its relatives would need `borrows` for the array of
  collections and `new` for what they take out, and one entry carries one marker. They are left off.
- **`JavaMarshal.GetContext(GCHandle)`** was not read, and has no entry.

## Working on the Analyzer

#### Projects

| Project | What it is |
|---|---|
| `Source/Singulink.CodeAnalysis` | The attributes and `NonCopyable` (`Move`, `ThrowIfNullRef`, `ThrowIfNullRefArgument`), for .NET Standard 2.0 and .NET 10 |
| `Source/Singulink.CodeAnalysis.Analyzers` | The analyzer, for .NET Standard 2.0, built against the compiler of the .NET 10 SDK (Microsoft.CodeAnalysis 5.0). It does not load in an older compiler |
| `Tests/Singulink.CodeAnalysis.Tests` | The tests, on .NET 10 with the compiler that the analyzer is built against, and on .NET 11 with the newest compiler, where that SDK is installed |
| `Tests/Stress` | Generators that write and run thousands of methods, run by hand; see its README |
| `Docs` | The documentation site, built with docfx by `build-and-serve.ps1` |

#### Where the Rules Live

There is one analyzer, `DoNotCopyValueAnalyzer`, split into partial files by concept:

| File | What it holds |
|---|---|
| `DoNotCopyValueAnalyzer.cs` | The registration, the options and the symbol list files |
| `DoNotCopyValueAnalyzer.Walker.cs` | The walker over the operations of a member, which every rule for values goes through |
| `DoNotCopyValueAnalyzer.NewValues.cs` | Whether an operation gives a new value or a stored one |
| `DoNotCopyValueAnalyzer.Members.cs` | Uses of members, the members that the compiler calls for `foreach`, `await` and a spread, and the inbound rule for code not built with the analyzer |
| `DoNotCopyValueAnalyzer.Conversions.cs` | Conversions that hide a non-copyable type |
| `DoNotCopyValueAnalyzer.Patterns.cs` | Patterns, deconstruction and `switch` |
| `DoNotCopyValueAnalyzer.ContainerLocals.cs`, `.ContainerUses.cs` | Locals that the method owns, and the one description of each use of a container |
| `DoNotCopyValueAnalyzer.Kept.cs` | Container parameters that must not be kept |
| `DoNotCopyValueAnalyzer.OwnedTask.cs` | The `owned` task of a completion source |
| `DoNotCopyValueAnalyzer.Moves.cs` | `[MovedFrom]`, generic moves and moves of containers |
| `DoNotCopyValueAnalyzer.Flow.cs`, `.FunctionGraph.cs`, `.LastUse.cs` | The paths through a function: the last use, the commit, and the path checks of `[MovedFrom]` |
| `DoNotCopyValueAnalyzer.WhatRuns.cs`, `.KnownReferences.cs` | What runs no other code and cannot throw, and which references are known to refer to something |
| `DoNotCopyValueAnalyzer.Declarations.cs` | Checks of declarations: fields, structs, attribute classes, the marks of types and members, erasable bases, covariant returns |
| `DoNotCopyValueAnalyzer.Assemblies.cs`, `AnalyzedAssemblies.cs` | The marker of an analyzed build, and SCA1025 |
| `NonCopyableTypes.cs` | Which types are non-copyable, containers, views or erasable, and which member uses are safe. A compilation has two instances: one for checked code, and one for the bodies of `[RequiresCopyable]` members, in which marked type parameters count as copyable |
| `SafeMemberList.cs` | The symbol list: the parser, the merge, and the built-in framework symbol list from the embedded `NonCopyableSymbols.txt` |
| `CarriedSymbols.cs` | What a container parameter may be used in, and what each of those operations carries |
| `DiagnosticIds.cs`, `Rules.cs` | The IDs and the descriptors |

The rules for values come from Roslyn's `RS0042` rule (`DoNotCopyValue`), and `Acquire` in `NewValues.cs` keeps Roslyn's name. It
returns `RefKind.None` for a new value, and any other `RefKind` for a stored one. How this analyzer differs from RS0042 is in
[Differences From Roslyn's Rule](Docs/articles/rules/roslyn-differences.md).

#### Messages

Every warning is a `DiagnosticDescriptor` in `Rules.cs`, made by `Create(id, title, message, description)`. The title, the message and
the description are constants at the top of the file. One ID can have several descriptors, each with a message for one case, and they
share the ID's title. All descriptors are warnings in the category Reliability, and the help link of each is the address of its
article: `https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/diagnostics/` followed by the ID in lower case and `.html`. That
address is fixed from the first release on.

`Rules.All` lists every descriptor, and is what the analyzer supports. A test fails if a descriptor is missing from it.

The tests compare the whole text of each message. `Tests/Singulink.CodeAnalysis.Tests/Verify.cs` has one helper per descriptor that
builds the expected message, so a change of wording in `Rules.cs` is a change in `Verify.cs` too. `MessageTests` pins the parts of
messages that say why: what made a local not the method's own, what makes a class a container.

#### Tests

| Test class | What it pins |
|---|---|
| `RoslynTests` | Roslyn's tests of its `RS0042` rule, for C#. A test that had to change says so |
| `ThirdPartyDefensiveCopyTests` | The test cases of the defensive-copy rules of hamarb123.Analyzers, with every struct and every type parameter marked |
| `BehaviorTests`, `StoredValueTests`, `LanguageConstructTests`, `CompilerCopyTests` | New and stored values, and the constructs and copies of the language and the compiler |
| `LastUseTests`, `NestedFunctionTests`, `KnownReferenceTests`, `WhatRunsTests` | The paths: the last use, functions inside functions, references known to refer to something, and what runs no other code |
| `MoveTests` | `[MovedFrom]` and `NonCopyable.Move` |
| `TypeParameterTests`, `UnmarkedGenericTests`, `GenericStructTests` | Marked and unmarked generics, `[RequiresCopyable]`, kept marks, and generic structs and views |
| `ListedMemberTests`, `OwnedTaskTests`, `ReturnedContainerTests`, `ContainerOwnershipTests`, `OwnershipListTests`, `ConversionTests` | Containers: what a listed member hands out, returning, keeping, owning, moving and converting them |
| `CarriedSymbolTests` | The list of `CarriedSymbols.cs` against the compiler's API, by reflection |
| `RecordAndCloneTests`, `BuiltInTypeTests`, `AttributeTests` | Records and clones, the ten built-in types and their property, and how attributes are recognized |
| `UncheckedCodeTests`, `UncheckedAssemblyTests` | Code not built with the analyzer: the marker, SCA1024 and SCA1025 |
| `SafeListTests` | The symbol list format, the merge, the built-in framework symbol list, and the diagnostics' help links and articles |
| `FrameworkScanTests` | The built-in framework symbol list against the shared frameworks (see [Symbol List](#symbol-list)) |
| `MessageTests` | The parts of messages that say why, and the fixes that the README and the guides give |
| `DeepCodeTests`, `EdgeCaseTests` | Deep and long code, and the paths that only unusual code reaches |
| `ProbeTests` | The probe projects under `Probes`, each with a `baseline.txt` of every warning by file, line, column and ID |

`ProbeTests` guards against a change that loosens the analyzer by mistake. The probes are programs written to find
what the analyzer misses or gets wrong, about 2,200 warnings in all. A warning that goes away shows there even where no test of its own
names the form. Changing a baseline is a deliberate edit that shows in a diff; set the environment variable `SCA_PROBE_WRITE` to a
folder to have a run write the warnings that it got there, in the form of a baseline.

Run the tests with `dotnet test Tests/Singulink.CodeAnalysis.Tests -c Release`. The stress tools in `Tests/Stress` are not part of
that run. Run them before a release or after a change to the flow rules; their README says how.

#### Adding a Diagnostic

A new ID touches all of these:

1. `DiagnosticIds.cs`: the ID, in the group of its range (see the table of groups in
   [What the Analyzer Does](Docs/articles/start/what-it-does.md)).
2. `Rules.cs`: the descriptor or descriptors, and each one in `Rules.All`.
3. `Verify.cs`: a helper per descriptor, with the whole message.
4. `Docs/articles/diagnostics/scaNNNN.md`: the article that the help link leads to.
5. `Docs/articles/diagnostics/toc.yml`: the article, in order of ID.
6. `Docs/articles/all-diagnostics.md`: a row in the table. No test checks this one.
7. `SafeListTests`: the list of IDs in `Diagnostics_HaveAHelpLinkToTheArticleOfTheirId`.

One test reads the `Docs` folder, `Diagnostics_HaveAnArticleOfTheirIdAndNoOtherArticleIsThere`. It fails where an ID has no article in
`Docs/articles/diagnostics`, where an article there is not the article of an ID, and where `toc.yml` does not list each article once,
in order of ID.

#### Changing the Built-in Framework Symbol List

1. Read the member in the runtime's source, and write what shows that it makes no copy in
   [NonCopyableSymbols.evidence.md](Source/Singulink.CodeAnalysis.Analyzers/NonCopyableSymbols.evidence.md).
2. Add the line to `NonCopyableSymbols.txt`. A member that exists only from some version of .NET on gets a trailing comment such as
   `# net10+`.
3. Run the tests on both targets: `FrameworkScanTests` checks the members that reach the ten built-in types against the runtime that
   the tests run on.
4. Update [Built-in Framework Symbol List](Docs/articles/rules/framework-symbol-list.md) where the change is one that a user would
   notice.

The evidence file is not part of either package, and is not embedded in the analyzer: only `NonCopyableSymbols.txt` is.
