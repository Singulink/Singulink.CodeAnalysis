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
- [Errors and Warnings](#errors-and-warnings)
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
stops and gives the member one warning, SCA1048, instead of skipping it or failing with an exception. The same holds for a walk over
types that reaches its bound where its answer has to name what it found, in a member or at a declaration (see
[Walks Over Types](#walks-over-types)). An exception of the analyzer is a test failure, in every test and for every generated input.

One exception goes the other way: **code that does not compile is allowed.** The analyzer runs while code is typed, and a half-written
line would otherwise get a warning next to the compiler's error. An operation that the compiler marked invalid, or whose type is or
mentions an error type, cannot run, and neither can a use of a value inside one within its statement: none of them is reported as a
use that the analyzer has no rule for (`CannotRun`), while the code of the member that compiles is checked as usual.

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

A read by value that is no last use only because the variable is used after it is a copy, and it is reported at the first use after it
on each path (SCA1016), with the read as an additional location: that is where the code goes wrong, as in
`queue.Enqueue(lease); lease.Dispose();`, where `Enqueue` was given the value and `Dispose` runs on the variable that still holds it.
The message leads with what happened, "'lease' is used after 'Queue<Lease>.Enqueue(Lease)' received its value, so that hand-off was a
copy", with the words of the hand-off in the past tense (`NonCopyableWalker.DescribeHandOff`), and its variants for a handler keep their
second halves after the same opening. A `using` statement over the variable is the one hand-off whose use can come before what the
statement does with the value: it takes a copy at its start and disposes the copy at its end, so a use in its body comes after the
hand-off and before the dispose. Its report has a message of its own (`Rules.UsedAfterUsing`), which says that, and advises a
declaration in the statement or a `using` declaration with a new value, since passing by reference or a move does not apply there.
A later use is every use that a path from the read reaches without passing another use; an assignment of the whole variable ends a
path, and an exception that can leave a block leads to its handlers (`MemberFlow.FindFirstLaterUses`). Only the place and the ID of
the report change, never what is accepted, and that is made sure of by how the report is moved: a second walk of the member takes
those reads for last uses, and a report of the first walk that contains such a read and that the second walk does not make is the
one that the read made (`DoNotCopyValueAnalyzer.HandOffs.cs`). The second walk runs only for a member that has such a report. A later
use is reported once, whatever hand-offs reach it, and a later use that is itself a hand-off is the hand-off for what follows it.
The report stays at the read where the read is its own later use, the next time round a loop, where a later use has no place of its
own in the source, where the later uses follow only the end of a `finally` that an exception leads to, and for every read that the
last use does not apply to: a field, a `ref` parameter, a captured variable, a variable that a reference can outlive, a read in an
exception filter, and two uses at one operation. A variable that holds a container is left out: what a read of one hands over is the
container, which is judged by whether the method owns it.

Each use of a variable has a place in the compiler's control-flow graph, in the order in which the code runs
(`MemberFlow.Classify`): a read where it stands, a call on the variable where the call runs, after its arguments, and a setter of a
property or an indexer, a store into a field and the accessor of an event where the assignment is made, after the value that is
assigned. A compound assignment, `??=` included, reads its target before its value runs and writes it after, so its target is used
at two places: in `lease.Uses += Reset(out lease)` the getter runs on the value that a hand-off before it gave away, and the setter
on the one that `Reset` assigned. Where the graph captures the target before a branch in the value, the read is where the target is
captured. An operator of the target's own type that is an instance member is called on the target in place, after the value, and reads
nothing first. A getter whose value the graph captures, as in an operand of `&&`, `||` or `?:`, runs where the capture is made, on its
branch only, not where the value of the operator is used after the branches meet. The earlier place of a reference counts on the paths
from every other use, and only the latest decides whether the reference itself is a last use (`VariableUse.IsEarlier`).

A hand-off to a member that the symbol list marks `borrowswhen` is followed only on the branch where the member has taken the value,
where the result of the call decides a branch (see
[Members That Take a Value Only Where They Say So](#members-that-take-a-value-only-where-they-say-so)). On the other branch the caller
still holds the value: a use there is no use after the hand-off, and it does not keep the hand-off from being a last use. So
`if (!writer.TryWrite(lease)) lease.Dispose();` is clean, since `ChannelWriter<T>.TryWrite` has `borrowswhen=false`. The read records the
blocks of that branch (`VariableUse.SuccessorsWhereTaken`), and the three walks from a read, for the last use, for the first later uses
and for the message about handlers, start from them in place of every successor of its block.

A member that throws may have taken the value. So the handlers of the block of a read follow every hand-off, whatever the entry of the
member says, and a `catch`, an exception filter or a `finally` that uses the variable after it is reported.

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

A read or a write of a static field is a use of a member too. It runs the static constructor and the static field initializers of the
type with the type arguments of the use, which is code of the generic as much as a member is: `Cache<Guard>.A` of
`class Cache<T> { static T A; static T B = A; }` copies a guard in the initializer of `B`. So the reference to a static field that is
not a constant is classified as a call is (`GetUse`, from `VisitFieldReference`), by the same principle for every generic: an unmarked
type parameter, of the compilation, of a checked assembly or of unchecked code, is judged at the use, and a marked one in the
generic's own build, where an initializer that copies its value is reported. A static property, method or event is a member use
already, and its line covers the static initializers that its use runs, so a field and those members are answered alike. A field of a
generic is answered by a line that starts with `F:` (see [Static Fields of Generic Types](#static-fields-of-generic-types)).

Delegates are exempt: a delegate type has no code of its own, and its target is checked where the delegate is created.

A struct of a non-copyable type must implement every interface member itself. A member that it leaves to the default implementation of
the interface runs on a boxed copy, also through a constrained type parameter, where nothing shows it.

The marks must be kept by overrides, implementations and delegates. Otherwise an unchecked body could sit behind a marked signature.
See [Keeping Marks on Overrides and Delegates](Docs/articles/rules/keeping-marks.md).

#### Type Parameters That Take No Type That Is Unsafe to Copy

Under the updated rules a report about a marked type parameter is strict, since it can stand for a type that is unsafe to copy. A generic
that is meant for plain non-copyable types, such as lock guards, would then answer for every copy of its type parameter with an unsafe
context. So the mark has three forms ([Types That Are Unsafe to Copy](Docs/articles/rules/marked-type-parameters.md#types-that-are-unsafe-to-copy)):
`UnsafeToCopy` not written or `true`, where the type parameter is unsafe to copy inside the generic and the generic takes any
non-copyable type; and `UnsafeToCopy = false`, where it is a plain non-copyable type inside and the generic takes no type through which
a value that is unsafe to copy can be reached. "Not written" and `false` differ, so the analyzer reads whether the argument is there, the named one or one for a
constructor parameter named `unsafeToCopy` of a mark of the project's own, in source and in metadata
(`NonCopyableTypes.GetUnsafeToCopyArgument`), and never the property. The property stays a `bool`, since an attribute argument cannot be
nullable, and its getter throws where it was not set, with `GetUnsafeToCopy()` returning `bool?`, as
`System.ComponentModel.DataAnnotations.DisplayAttribute` does for `AutoGenerateField`, so that reading it never passes "not set" off as
`false`.

A type argument through which a value that is unsafe to copy can be reached, for a type parameter that says `false`, is SCA1026
(`NonCopyableTypes.LetsReachUnsafeToCopy`). What counts is what other code can reach (see [What a Report Is About](#what-a-report-is-about)),
and not only what a copy duplicates: a type that is unsafe to copy, a container of one at any depth, a tuple or a struct that carries
one, and a delegate or another generic class over one. Inside the generic every report about the type parameter is the ordinary
warning, which its author may suppress, and that holds for a value of it that other code is given as much as for a copy: a
`Box<List<UG>>` would hand the list to an unlisted member, or hide it behind `object`, with warnings only, and its caller would get no
report at all. So the boundary is drawn by reach, and the generic is given nothing that a report inside it would have to be strict about.
A type parameter of the generic around the use counts as unsafe to copy there unless it says `false` itself, and a type over it counts as
a container of such a type; one that is not marked is copyable there, and is judged where its own generic is used. The reports name the
type as unsafe to copy where it is one, and say that a value that is unsafe to copy can be reached through it where it is not
(`ChooseBindingNotTaken`). The check is the one of an unmarked type parameter one level up, and goes through the same machinery: `GetUse`
attaches a `BindingNotTaken` to the classification of a member use (`FindUseBindingNotTaken`), and `CheckMemberUse` reports it besides
whatever else the use is. It changes nothing else, since the generic is checked code. So every member use that SCA1020 sees is covered:
a type named in code, a static member, a nested type, type arguments written or inferred, an extension, the members that the compiler
calls, a method group that is made into a delegate, and a static field that is read or written. Naming a type runs no code of the
generic and is not reported, as for SCA1020.

Code of the generic can also run with no member use that shows the binding, and those routes are covered where the binding is made:

| Route | Where it is reported |
|---|---|
| A base type or an interface: the inherited code runs through overrides that are called through a base class that is not generic, and through interface members that the type inherits. The binding can be of the base type itself, or of a type that it is given at any depth of its type arguments, as in `Runner<SS<UG>>`, whose members that code can run | The entry of the base list (`AnalyzeBaseListBindings`, through `FindBindingNotTakenWithin`, as a member use) |
| A type that the `new()` constraint lets a generic create: its parameterless constructor and the base constructors that it calls with nothing written. The constructor that the compiler gives a struct runs nothing | The use of the generic with the `new()` constraint |
| A struct over the type that is boxed, by a conversion, a pattern or a `foreach`, whose interface members then run on the box | The box (`FindStructBindingNotTaken`, through struct type arguments at any depth), as `IsStructOfUncheckedCode` does for a struct of unchecked code |
| A type with the binding given to other generic code, as the type argument of a member that is used or at any depth inside one (`List<SS<UG>>`, `Dictionary<int, SS<UG>>`), to a type parameter that is marked or not: that code can run its members with nothing that shows them, `ToString`, `Equals` and `GetHashCode` through any type parameter, a static abstract or virtual member and an interface member through a constraint, and the members of the elements of a collection, as `List<T>.Contains` runs `Equals` | The use of that generic (`FindUseBindingNotTaken`, `FindBindingNotTakenWithin`), unless the use is reported for that type parameter already (SCA1020, SCA1021, SCA1022, SCA1023), which is strict for such a type. One report per binding at one place: a `foreach` runs `GetEnumerator`, `Current` and a box at the same place |
| A struct over the type held inline in a field of a struct that does not override both `Equals` and `GetHashCode`: the ones that it inherits from `ValueType` run the members of the fields | The field (`AnalyzeFieldBindingsNotTaken`). A record is left out: its fields of a non-copyable type are reported as such. A ref struct is left out too: the compiler lets no code call the `Equals` and `GetHashCode` that it inherits |

A class over the type is always created through a constructor, which is a member use. A delegate type has no code of its own, and its
target is judged where the delegate is created.

The line between a use and naming the type: a field, a parameter, a local or an array of `SS<UG>` or of a type that holds it, `typeof`,
`default`, a read of an instance field, a constant and `nameof` run no code of the generic and get nothing. Code that can run the members of `SS<UG>` gets the report:
a member of it, generic code that it is given to through any type parameter, and the inherited equality of a struct that holds it inline.
What is not followed is code that names no type: reflection, `dynamic`, and the members that the compiler writes for a record or an
anonymous type, whose own fields of a non-copyable type are reported already.

An override or an implementation may say `false` only where the type parameter of the member that it overrides or implements says it
too, or is not marked (SCA1040, in `CheckMarksOfMember`, with the other marks): otherwise a call through that member binds a type that is
unsafe to copy. Where the type parameter of that member is not marked, a call through it with any non-copyable type is reported already
(SCA1020), so a mark with `false` there is allowed as a plain mark is: marking more only makes the body stricter, and `false` is checked
where the override is used directly (SCA1026). A member that does not mark the type parameter at all does not say `false`. The two parts of a partial method share the attributes of
their type parameters, and the compiler rejects the attribute written on both, so they cannot differ. A lambda has no type parameters,
and a generic method that is made into a delegate is a use of the method with inferred type arguments, which SCA1026 covers.

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

What SCA1030 says depends on what the member hands out. A value of a type that is bound to the member, as from an indexer, `Current`
or `Result`, is a copy of what the stored container holds (`ReadFromStoredContainer`). Any other value is new and reads what the
container still holds, as the enumerator that `GetEnumerator` makes of a stored list does (`MadeFromStoredContainer`), and a
constructor makes one as well (`CreatedFromStoredContainer`). The rule and its severity are the same; only the words differ, so that
neither calls a new enumerator a copy.

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

**Erasable for some type parameters.** The key collection of a dictionary hands out keys and never a value, and its type still names
the value type, so `map.Keys.ToList()` hid the value type of a dictionary of guards. `erasable` takes `for=` with type parameter names,
of the type or of a type that contains it: no base type and no interface hands out a value of those type arguments, so a conversion to
one of them hides nothing of them (`SafeMemberList.IsErasableFor`). `ExposesNonCopyableType` skips what is bound to those type
parameters and goes on with the rest, and a type that derives from such a type is held to SCA1043 as for an erasable one
(`IsPartlyErasable`). Two lines merge to the type parameters that both name, and a line of a file that would widen a built-in `for=`
is left out with a warning, as for a member. Such a line claims more than the signatures show: the code behind those interfaces must
not copy such a value either, since the conversion hands the object to it. So every line of the built-in framework symbol list was
read in the source as well as checked against the reference packs (`ErasableLines_...` in `SafeListTests`). The key and value
collections of `Dictionary<,>` read their own half of an entry only; those of `SortedDictionary<,>` and `OrderedDictionary<,>` copy
whole entries, and those of `ReadOnlyDictionary<,>` wrap any dictionary, so they have no line.

The erasure applies whatever the other type parameters hold. A conversion is judged by what the source exposes
(`GetExposedNonCopyableTypes`): what it mentions, with what an erasable type holds through the type parameters that it is erasable for
left out, at any depth. The question whether the conversion hides a non-copyable type and the severity of its report both read that, so
the key collection of a `Dictionary<PG, UG>` converted to `IEnumerable<PG>` hides nothing, and converted to `IEnumerable` hides the keys
only, which is the ordinary warning about `PG`. The erasure is about the base types and the interfaces and nothing more: what the type's
own members hand out still counts where other code holds the type itself, as `Lazy<int, UG>.Metadata` hands out the metadata, so what
other code reaches through a type (`ReachesUnsafeToCopy`) still counts every type argument.

[Boxing a new value](Docs/articles/rules/boxes.md) is a move into the box: nobody else holds the value, and inside the box it is one
object that every holder of the reference shares. A generic struct that is non-copyable only through its type arguments is different.
Its box runs unchecked code: `ITuple` and `IEnumerator` hand out a boxed copy of the element at every read. So boxing one is a hiding
conversion. `Nullable<T>` is the exception: its box holds the value itself.

The box of any other generic struct hides what its type arguments make reachable as well, also where its own code is checked: a boxed
`Mk<List<UG>>` lets other code reach the list with a later cast or reflection, as `List<UG>` converted to `object` does. So a box of a
generic struct whose type arguments carry a container, at any depth, is SCA1023 (`OpaqueBoxingOfContainers`, through
`HidesContainersOfTypeArguments`), for a new value and a stored one, by a conversion, a conversion with no node, a pattern and a `foreach`
alike, and counts by what the struct is made of, as the box of a tuple does. A struct whose type arguments carry no container hides
nothing that its box does not show: its value moves into the box. A binding that a type parameter does not take (SCA1026) is the report
of such a box where there is one, since its fix, another type argument, answers both.

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

The analyzer reads the named argument `UnsafeToCopy` of the attribute and never calls it, so a project's own `NonCopyableAttribute`
needs only a settable `bool` property or a public `bool` field of that name for the argument to count. It also reads an argument for a `bool` parameter of the
constructor named `unsafeToCopy`, in any case (`NonCopyableTypes.GetUnsafeToCopyArgument`), with the named argument first, as the
property setter runs after the constructor. Both are what metadata shows where the attribute is applied, in source and in a referenced
assembly alike.

What it cannot read is the code of the attribute class. A derived mark whose constructor sets `UnsafeToCopy = true` gives a struct
that is plain non-copyable to the analyzer, which fails in the dangerous direction and in silence. So code of a recognized mark of the
compilation that sets the value gets SCA1049, a plain warning, in the project that declares it: an assignment of the property or of
the field of that name, the initializer of either, and an argument for a parameter named `unsafeToCopy` of another constructor
(`AnalyzeUnsafeToCopySetInOwnCode`). Setting the property or the field from the constructor's own `unsafeToCopy` parameter, or passing that parameter on, is exactly what the analyzer reads, and
gets nothing. A property whose getter returns something other than what its setter stores is not followed. The constructor parameter
is what makes a mark that cannot work either way, `[UnsafeHandle(true)]` with a constructor that sets the property or
`: base(unsafeToCopy: true)`, expressible at all: `[UnsafeHandle(unsafeToCopy: true)]`.

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

The reference to a declaring assembly says nothing of three kinds of value. The eleven built-in non-copyable types are declared by the
framework, which every assembly references, or every assembly of WPF for `DispatcherProcessingDisabled`. A struct of unchecked code that only a symbol list file makes non-copyable is the same: a
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
- **A package is judged by what it depends on.** The code of a package can use only what it was compiled against, and NuGet
  gives a project the packages that a package names as its dependencies, and theirs. The targets file of the package gives the
  compiler the assets file that NuGet writes for the project (`project.assets.json`) as an additional file, and `PackageGraph`
  reads from it what each package depends on. A package that comes with a reference assembly is reported only for an assembly of
  a package in its chain of dependencies, or of its own package. Where the chain has a project of the solution, or an ID that
  the file has no entry for, the file does not name the assemblies, and the package is also reported for an assembly that is of
  no package.
- **That rests on one assumption: a package names what it is compiled against as its dependencies.** One that does not, such as
  one that references a library with private assets, fails in every project that lacks the library, and is not reported.
- **A project is not judged by the file.** The file names the assembly of a project by a placeholder, and it leaves out what
  the project references with private assets. Every target of the file is read, as the compilation does not say which one is its
  own: an assembly is out of reach only where it is in every target that has the package. A build with no such file, or with one
  that cannot be read, judges every reference assembly like a project's. The file is read by a small reader of its own, as an
  analyzer cannot count on a JSON library in every host.
- **For a project the check is the cautious reading, in two ways.** The assembly may use nothing of the other one, and the
  `trusted` line says so. The other assembly can be built on the unchecked one and use it only inside its methods: its own
  reference assembly does not show that, so the unchecked one is reported though it cannot use the other. Making every project
  compile against implementation assemblies (`CompileUsingReferenceAssemblies`) would show the real references, and would slow
  every incremental build.
- **Internal members are not imported from a referenced assembly,** so what they let out cannot be seen. An unchecked assembly that
  checked code names in an `InternalsVisibleTo` attribute can use them, and gets the warning whatever that code holds. This also
  covers an internal `[NonCopyable]` struct, which the check of the declaring assemblies cannot see either.

## Symbol List

#### Closed and Read One by One

The built-in framework symbol list names members and static fields exactly, per overload, with no entry for "every member of a type" and no wildcard. Each
entry was read in the runtime's source, and the evidence is in
[NonCopyableSymbols.evidence.md](Source/Singulink.CodeAnalysis.Analyzers/NonCopyableSymbols.evidence.md), beside the list. The criterion is
the one of the principles: a member is on the list only if it makes no copy other than the value that it hands out. Members of
`Unsafe`, `MemoryMarshal` and the other marshal classes are on the list for that one fact. The analyzer guarantees nothing about code
that uses them to do what the language otherwise rules out (see "Members That Make Read-only Storage Writable" in the evidence file).

The scope of the list was chosen by who uses non-copyable structs. They are a low-level tool: in nearly all code a guard is a local or a
field, and sometimes an array element reached through a span. So the list covers thoroughly what such code uses (spans, memory, arrays,
the marshal classes, pools, `Interlocked`, everything that `await` calls), and the general-purpose collections get their basic members.
A gap there costs a warning that a project can suppress, or answer with a line of its own symbol list file.

`FrameworkScanTests` keeps one part of the list complete. A member of the framework that takes one of the built-in types other than
by value needs a `borrows` entry, and one that hands out a container of one needs `new` (see
[Code Not Built With the Analyzer](#code-not-built-with-the-analyzer)). The test loads every assembly of the shared frameworks that the
tests run on, finds every such member, and fails where one has no entry with the marker that it needs. Since it reads the runtime that
the tests run on, each test target scans its own version of .NET. `DispatcherProcessingDisabled` is of WindowsBase, which of the
shared frameworks only Microsoft.WindowsDesktop.App has; where that is not installed, as on a machine that is not Windows, the scan
does not expect it. The .NET Framework 4.8.1 is scanned as well, where it is installed
(see below).

#### The .NET Framework 4.8.1

The analyzer works in a project for the .NET Framework as in one for .NET, and the built-in framework symbol list is checked against
the .NET Framework 4.8.1, its last version, as it is against .NET:

- **How.** Each scan of `FrameworkScanTests` has a second test, `NetFramework_...`, that reads the implementation assemblies of the
  .NET Framework 4.8.1 of the machine: every assembly that `RedistList/FrameworkList.xml` of its reference assemblies names, 235 of
  them, from the global assembly cache or from the folder of the runtime. The list comes from the NuGet package
  `Microsoft.NETFramework.ReferenceAssemblies.net481`. The reference assemblies have no method bodies, so they serve to resolve lines
  of the list, and not for the scans. A name that the list has and the machine does not fails the test, except
  `System.Activities.Statements`, which exists neither as a reference assembly nor in the runtime.
- **Where.** The scans read the same assemblies on every target of the tests, so they run in the test run of net10.0 only. Elsewhere, on
  a machine that is not Windows, and on one whose registry has no release of 4.8.1, each is skipped as inconclusive with a message
  that says why. The output of a test run lists the skipped tests with their messages, so a run shows whether they ran. The deepest
  scan follows every virtual call to the overrides of every class below the called one, and works those out once per member that is
  called: walked again at each call, as they were at first, they made it take 366 seconds on the .NET Framework and 163 on .NET 10,
  against 13 and 10 now, with the same results.
- **What they check.** What the scans of .NET check: the members that get at a built-in non-copyable type other than by value, the
  classes that clone themselves, the members that clone, and the members of a class whose line says `clones=complete` whose object
  reaches other code. The .NET Framework has four of the eleven built-in types in the box, `SpinLock`, `GCHandle`, `AsyncFlowControl` and
  `DispatcherProcessingDisabled`, and the test of the members checks that it finds exactly those four. No member of it needs an entry that .NET does not.
- **Lines that only the .NET Framework has** end in the comment `# netfx`, as a line of a member that exists from .NET 11 on ends in
  `# net11+`. The parser needs nothing for it: a line applies to its ID wherever the ID resolves, and names nothing elsewhere.
  `SafeListTests` resolves each such line against the reference assemblies of 4.8.1, reads them as a symbol list file there, and
  fails for one that names a symbol of .NET 10 or .NET 11, where it would apply as well. They are the `clones` lines of the seven
  classes that clone themselves and that only the .NET Framework has, and of their six public `Clone` methods. Their assemblies are
  not in `FrameworkAssemblies.txt`, so a class that derives from one got the warning of an unknown base class before; it now gets the
  one of a class that clones itself.
- **What was judged.** The scan of what reaches other code flags 14 members of `PrinterSettings` and `PageSettings` there that it does
  not flag on .NET. They have no line, since a line applies on every framework and would add warnings on .NET, and one of them is
  `PrinterSettings.ToString`, which no `clones` line may name (see the row of the code that the compiler writes for a record below).
  Nine members of `PrinterSettings`, `ToString` among them, reach code that can throw `new InvalidPrinterException(this)`; the
  exception keeps the settings, and they get out of it only through its `GetObjectData`, to a serialization. That is judged not to
  be a hand-on that matters: no member of the exception hands them to code, and a formatter does with them what it does with any
  serializable object, which `PrinterSettings` is on its own. On .NET the exception keeps nothing. Five members of `PageSettings`
  give `new HandleRef(this, handle)` to native methods, and a `HandleRef` keeps nothing past the call. Both are entries of the
  evidence. And `MemberwiseClone` has no body on the .NET Framework, so the scan, which takes a method with no body to keep its
  arguments, would flag every `Clone`; it is taken to keep nothing, since the clone itself is what the scan of what clones finds.
- **No test of the assembly names.** `FrameworkAssemblies_NameEveryAssemblyOfTheSharedFrameworks` has no counterpart for the .NET
  Framework. Its assemblies that `FrameworkAssemblies.txt` names are among those that the scans read, so what the list says of them is
  checked. The others, such as `System.Messaging`, are other libraries for the analyzer by design, and a fixed list of their names
  would pin a set that cannot change.
- **No package is read.** A package that brings part of .NET to the .NET Framework, such as System.Memory, can have the name of an
  assembly of a shared framework, and the analyzer then takes it for the framework. The scans do not read packages. A one-off read
  of System.Memory, System.Buffers, Microsoft.Bcl.AsyncInterfaces, System.Threading.Channels, System.Collections.Immutable and
  System.Runtime.CompilerServices.Unsafe for the .NET Framework found nothing that needs a line, and no test repeats it.

#### Merging

Where two entries disagree, the one that claims less applies. So a symbol list file can add entries and tighten a built-in one, but cannot
loosen it. A type that one entry calls `unsafetocopy` and another `noncopyable` is unsafe to copy: that line only adds errors (see
[Unsafe to Copy](#unsafe-to-copy)). A `noncopyable` or `unsafetocopy` line for a type that already is what it says, by the attribute that
the analyzer recognizes on it, has no effect, and gets SCA1042 in the project that owns the file, as a `borrows` line that claims more
than the built-in entry does (`SafeMemberList.GetWhyNonCopyableHasNoEffect`): no line of a file does nothing in silence. A type that is
unsafe to copy is non-copyable as well, so a `noncopyable` line for one gets it too, and an `unsafetocopy` line for a type that is only
non-copyable has an effect and gets nothing. A `copyable` line that takes the type out gives the line an effect, since the line wins
over it. A line for a type of the built-in list gets nothing in any project, as a `copyable` line for one does: one file can serve a
project that includes the built-in non-copyable types and a library that leaves them out, and the second needs the line. Warning in
the first, as the analyzer once did, asked the author to take out a line that the other project needed. The `copyable` marker is one
exception, against the built-in framework symbol list: taking a type out is what the marker is for. `clones=complete` is the other: it counts wherever a line says it, against a plain line of a file or of the built-in list (see
[Classes That Clone Themselves](#classes-that-clone-themselves)).

A `copyable` line counts only in a file of the project being built, and never in one that came in a package (`IsFileOfAPackage`). A
library's props file adds its file from the folder of its package, so a file is a package's where it is under a folder that NuGet
restores packages to or from: the global packages folder (`NuGetPackageRoot`), every folder that the assets file of the project names
under `packageFolders`, the fallback folders included, and for a project that restores with `packages.config`, which has neither, the
folder that holds the folder of the analyzer's own package (`_NonCopyablePackagesFolder`, which the targets file works out). The list of
folders is read from the assets file and not from `NuGetPackageFolders`: the compiler reads a `;` in the value of a build property as the
start of a comment, so only the first folder would arrive. A path that has a `#` or a `;` in it is cut short in the same way; the assets
file names the global packages folder as well, in full. A build that gives none of the folders counts every file as the project's own. It says that the project copies the type on purpose, which is the decision of one project: a library's
line would take the type, even one that is unsafe to copy, out for every project that uses the library, with nothing to show it. A
library's own build reads its file as a file of the project and honors the line. A `copyable` line of a package's file gets no SCA1042
in the project that uses the package, which has nothing to fix, and gives no `noncopyable` line an effect there.

An entry that names something the project does not reference is ignored, so that one file can serve several projects. A `noncopyable`
line that names nothing gets a warning all the same: a mistyped ID there would leave a type copyable in a build that looks clean.

There is no MSBuild property to mark a type or take one out. The symbol list is the one place for both.

#### Members That Take a Value Only Where They Say So

A member that is given a value by value takes it, as far as the last use goes. Some members take it only where they return true, as
`TryAdd` and `TrySetResult` do, and the caller still holds the value where they return false. The marker `borrowswhen` of a method entry
says so. It is named after `borrows`, since it makes the same claim, that the member uses what it is given during the call and keeps
nothing of it, limited to one result of the call. Its value is that result, `true` or `false`. Where the member returned the other
result, or threw, it has taken the value.

| Entry | Meaning |
|---|---|
| `borrows` | Keeps nothing of the value, whatever happens |
| `borrowswhen=false` | Keeps nothing of it where it returned false; has taken it where it returned true or threw |
| `borrowswhen=true` | The mirror image of `borrowswhen=false` |
| No marker | Takes the value, whatever happens |

A member that throws may have taken the value, whatever its entry says (see
[Alternatives That Were Given Up](#alternatives-that-were-given-up) for the outcome that would have said otherwise).

The marker was first written the other way round, as `takeswhen` with the outcomes in which the member has taken the value. That form
listed what the member does in the common case, so that an outcome left out of it was a claim that the member keeps nothing there.
`borrowswhen` lists the claims: nothing is implied, and an outcome that a line leaves out, or that the analyzer does not know, is the
safe reading. `takeswhen` was removed with no alias before a release had it.

The rules of the marker:

1. **The outcomes come in any order, each once,** separated by commas, with white space allowed around the `=` and the `,`, as around
   the `=` of `for=`. One marker per entry, and `for=` can follow it, as for every marker:
   ``M:...Dictionary`2.TryAdd(`0,`1);borrowswhen=false;for=TValue``.
2. **It needs a method that returns `bool`, `Task<bool>` or `ValueTask<bool>`,** and is called by its name; for a task the outcome is
   the awaited result. A constructor, an operator, a conversion and an accessor have no call that the analyzer follows for the marker,
   and their lines get SCA1042.
3. **`true` and `false` together are rejected,** with a reason that points to `borrows`: they would say that the member takes the value
   only where it throws, which no member was found to do.
4. **An outcome that the analyzer does not know is skipped,** so the line claims less. The project that owns the file gets SCA1042 for
   it, as for a line that widens the built-in list (`IsFileOfAPackage`); a project that gets the file from a library gets nothing, since
   it has nothing to fix. A line whose every outcome is unknown is a line with no marker. A whole marker that the analyzer does not know
   leaves the line out, as before.
5. **It is about values only.** It covers every parameter that the member takes by value whose type in the member's definition is a type
   parameter that can be a struct, or a struct, where `for=` covers every type parameter that the type mentions. Only a value of a
   non-copyable type is followed. Unlike `borrows`, it says nothing of a container that the member is given through a parameter over
   its type parameter, and has no effect on the keeping check.
6. **A stored value is still a copy.** A field or another stored value that is passed by value to such a member is reported as before:
   in the outcome where the member takes it there are two.
7. **The branch is followed only where the result of the call goes straight into a condition,** the way `[NotNullWhen]` narrows only
   there. The compiler's control-flow graph splits `if`, `while`, `do`, the condition of a `for`, `!`, `&&`, `||` and `?:` into blocks
   that branch on the call, also in a `return` and in the value of a conditional, so the call or the operator around it is the value
   that a block branches on. Around the call the analyzer follows `!`, `==` and `!=` with the constant `true` or `false` on either side,
   and `is true`, `is false`, `is not true` and `is not false` (`MemberFlow.FindSuccessorsWhereTaken`). A result that is stored in a
   local, passed on, or combined with `&` or `|` gets the normal judgement, as if the entry had no such marker. A call that returns
   `Task<bool>` or `ValueTask<bool>` must be awaited where it is made, directly or behind `ConfigureAwait` of the task's own type; a
   task that is stored first is judged as any other hand-off.
8. **The merge takes the outcomes that every line lists.** An entry is the set of outcomes in which the member borrows, `borrows` the
   full set and a line with any other marker, or none, the empty set, and the entry that claims less applies, as everywhere:
   `borrows` and `borrowswhen=false` merge to `borrowswhen=false`, and `true` and `false` to no marker
   (`SafeListEntry.BorrowedOutcomes`).

Besides that, an entry with the marker is an ordinary entry: the member is safe to use, as with no marker. It is a claim that the
analyzer does not check, like `borrows`, also for a method that the project declares itself. A line that cannot work gets SCA1042: the
marker on a type, a property or an event, an empty or repeated outcome, `true` with `false`, the marker on a method that is not called
by its name or that returns anything but `bool`, `Task<bool>` or `ValueTask<bool>`, and a method with no parameter that the marker can
cover.

Rule 8 has a consequence: a file cannot add the marker to a member that the built-in framework symbol list names without it, nor add an
outcome, since the entry that claims less applies. A `borrowswhen` line that lists an outcome which the built-in entry does not list
gets SCA1042 for it in the project that owns the file, as a line that names a member for more type parameters does
(`SafeMemberList.FindOutcomesBeyondBuiltIn`). A `borrows` line for a member that the built-in entry names with `borrowswhen` or with no
marker claims more as well, and gets SCA1042 in the same way (`SafeMemberList.FindBorrowsBeyondBuiltIn`). It does not where its `for=`
leaves out a type parameter that the built-in entry covers: that line takes the entry away for the type parameter, which is an effect.
Nor does it over a built-in entry with another marker, such as `new`, which the line takes away.

The built-in list has `borrowswhen=false` for the members that return false before they keep anything of the item:
`TaskCompletionSource<T>.TrySetResult`, the `TryAdd` of the two dictionaries and of their alternate lookups, the four `TryAdd` of
`BlockingCollection<T>`, and `ChannelWriter<T>.TryWrite`. The evidence file shows, for each, where it returns false
("Members That Keep Nothing of a Value in Some Outcomes"). The adds that return nothing, such as `List<T>.Add`, have no marker.

#### Static Fields of Generic Types

A static field of a generic type is answered by a line of its own, by its documentation comment ID (`F:`). The line says that the
static constructor and the static field initializers of the type make no copy of a value of the type arguments. It is looked up as a
member line is (`SafeMemberList.GetMember`), and merges with another line for the field as member lines do.

- **The markers.** `for=` narrows the line to type parameters, as on a member, since the initializers of a type can copy a value of
  one type argument and not of another. No marker fits. `new` speaks of what a member hands out: a read of a field gets the value that
  the field holds, the same for every reader, so it is never new. `borrows` and `borrowswhen` speak of what a member is given, and a field
  is given nothing. `drains`, `owned` and `clones` speak of a sequence, a task and a clone. Each is rejected as a line that cannot be
  parsed, with a reason, as a marker that does not fit its member is.
- **A line that no use is judged by.** A line for an instance field, a constant, or a static field of a type that is neither generic nor
  nested in one has no effect, and gets SCA1042 with the reason (`SafeMemberList.GetWhyFieldHasNoEffect`). That is a warning that the
  line has no effect, and not one that it cannot be parsed: the line is well formed and names a field, as a `clones` line for a sealed
  class names a class, and the reason says what is missing. A line that names nothing is ignored, as every member line is.
- **The reports.** A field that is not on the list gets SCA1020, or SCA1023 for a container, with texts of their own that say what a read
  or a write of the field runs and give the `F:` line. A line for some type parameters only gets the texts of a member that is listed
  for others.
- **The framework.** `FrameworkScanTests` finds every public or protected static field, other than a constant, of a generic type of
  the shared frameworks and of the .NET Framework 4.8.1, and fails for one with no line. Every type parameter counts, one that is
  constrained to a class included, since a container can be bound to it. The built-in list has 12 such lines; what each static
  constructor does is in the evidence file, beside the list. `XmlQuerySequence<T>.Empty` is public in an implementation assembly of
  .NET that no reference assembly exposes, and its line is marked `# netfx`: it resolves in the reference assemblies of the .NET
  Framework, and answers the scan of .NET as well.

#### Built-in Types Through a Library

The eleven built-in non-copyable types are on in a project that references one of the two packages, and off in a project that gets the
analyzers only through a library. A library that marks its guards brings the analyzers to everyone who uses it, so that they cannot copy
those guards. It must not bring warnings about their own `GCHandle` code with it. See
[Leaving out the built-in non-copyable types](Docs/articles/guides/built-in-types.md).

The default is set by the package's targets file, and not by a props file, because the project's items are not known when props files
are read. The analyzer takes only `true` for on, so that a build in which the targets file did not run has them off.

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
  platform has, such as `Mono.Android`: 80 of the 131 reference assemblies of .NET Framework 4.8 and 4.8.1 are not in the file (their
  folder has 133 files, of which `System.EnterpriseServices.Thunk` and `System.EnterpriseServices.Wrapper` are not assemblies), and
  88 of the 236 assemblies that their `FrameworkList.xml` names, facades included. The scans read those of 4.8.1 all the same (see
  [The .NET Framework 4.8.1](#the-net-framework-481)), and a class of them that clones itself has a line. The name was
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
- **The list is not picked by hand.** `FrameworkScanTests` reads the IL of the shared frameworks of both targets, and of the .NET
  Framework 4.8.1 where it is installed, finds every call of
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
   class between it and the class that clones itself, is judged by its own line and its own signature. Nor does it vouch for what the
   member's code runs below its class: a virtual call on its own object runs the override of a class below, so a member of a base class
   is judged by those overrides as well (the row "A member of a base class that runs an override" of the table below). What a signature does not show,
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
| A member of a base class that runs an override of a class below it, where the override clones the object: `CultureInfo.GetFormat` returns `NumberFormat`, which a class of a library overrides with a getter that clones the culture | Yes | Checked: `FindMemberBelowThatCanClone`. The analyzer cannot tell which member of a base class calls which virtual member, so each member of the base class is judged by every member below its class that its code can run: an override of a member that the class has or inherits, and a member that implements a member of an interface in a class below, of any interface, since the code of a class can test its object for an interface that it does not implement. The walk goes down from the member's class, and a class with a member that can be run adds its own virtual members to what the classes below it can replace, since the code of that member can call them. Such a member can clone where a `clones` line names it, in any assembly, or where it is of an assembly that was not built with the analyzers and neither a line of its own nor a `clones=complete` line that covers its class says what it does. In a compiled class, an explicit implementation can be the member that the compiler writes to hand a call on to an inherited member, where the class lists an interface again; its code cannot be read, so the inherited public member that fits the interface's member counts as well. For a property or an event the compiler writes only the accessors, so the class then has no member that implements it, and the inherited one counts alone. A line for the inherited member does not vouch for the compiler-written one, which cannot be told from one written by hand; a `clones=complete` line for the class does. Four things are left out: a member of the class that clones itself or of a class above it, which the lines of that class speak for as one piece of code, where one of them runs another included, and which only passes the call on to the classes below, with one exception: an override there that a `clones` line names, where the member that is used has no line of its own. The lines then say that the override clones and nothing of the member above it that can run it, as with a template method whose step the class that clones itself overrides with a clone, and nothing else tells the author of the lines so (SCA1008 looks at the members of the class that clones itself only). The member gets the warning until a line of its own says whether it runs the override. A member there that implements an interface is no such exception, or a class that clones itself and implements `ICloneable` would put the warning on every member of the class above it; a member that takes the place of one that clones already, since a member that runs it counts as one that clones by the lines, by its own line or by what `clones=complete` says of a member with none; a member of a class of the framework, whose overrides the scan of the framework followed; and everything where the base class is `object`, whose code calls no member of its object that a class can replace. An event's accessors are members like any other, so removing a handler is judged as adding one is, except for the handing on of the object to the handler. A constructor of a base class that runs an override is no use of a member: the object is created where its binding is written, which has its warning | `CloningUseTests.Member_OfABaseClassGetsAWarningWhereAnOverrideBelowItClones` and the tests after it |
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
  no body to read, are taken to keep nothing, and so is `MemberwiseClone` of the .NET Framework. Both passes also run on the .NET
  Framework 4.8.1, with what was judged there in [The .NET Framework 4.8.1](#the-net-framework-481).
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

## Errors and Warnings

The memory safety rules of the compilation decide how the analyzer reports, through `UnsafeContext.UsesUpdatedRules`.

**Under the current rules** every rule is an ordinary warning, as in release 0.7.5: `#pragma`, NoWarn, `[SuppressMessage]` and severity
configuration all work as for any analyzer warning, at a declaration as well, and whether code is in an unsafe context plays no part.
The model of errors below belongs to the updated rules for two reasons. Under the current rules the language itself makes no promise
about code outside an unsafe context, so an error that cannot be suppressed would promise more than the compiler does. And marking
one place needs what only the updated rules and their language version have: the `unsafe(expr)` expression, and `await` in an unsafe
context. Without them a hand-off that is awaited can only be covered through an extra local.

**Under the updated rules** a copy of a value of a type that is [unsafe to copy](#unsafe-to-copy) is a memory safety defect, so the
analyzer reports it as an error that cannot be suppressed. The one place where code may take that on itself is an unsafe context, where
the language already lets the author answer for what the compiler cannot check: there a copy is a warning, suppressed next to the code.
Under the updated rules only an `unsafe` block, an `unsafe(expr)` expression and the initializer of an `unsafe` constructor are unsafe
contexts, so the author names the place. A declaration is in no unsafe context, and the language has its own way to answer for one:
the modifier `unsafe` or `safe` on the declaration itself, which the compiler already requires on a field of an explicit layout type and
on an `extern` member. So a report at a declaration marked with either is the same warning (see
[Marked Declarations](#marked-declarations)).

**A report about any other non-copyable type** is reported under the updated rules exactly as under the current rules: the ordinary
warning, with the message of the current rules, which every suppression reaches, and an unsafe context and the modifiers play no part.
A copy of a lock guard is a logic error, not a memory safety problem. An error that only `unsafe` answers for would make `unsafe` stand
for more than memory safety, and code that copies a guard on purpose would need an unsafe context that says nothing about memory. Only
the author of a type knows whether a copy of it can break memory safety (a handle freed twice, memory unpinned while a pointer into it
is live) or only the logic, so the type says so.

Each ID is in one of three groups, which `Rules.GetGroup` holds:

| Group | IDs | Current rules | Updated rules, about a type that is unsafe to copy, in code in an unsafe context and at a marked declaration | Updated rules, about a type that is unsafe to copy, everywhere else | Updated rules, about any other type |
|---|---|---|---|---|---|
| Enforced (`RuleGroup.Enforced`) | SCA1001 to SCA1017, SCA1020 to SCA1024, SCA1026, SCA1030 to SCA1033, SCA1040, SCA1041, SCA1043, SCA1044, SCA1046, SCA1048 | Warning, suppressible in every way | Warning: `#pragma`, NoWarn and severity configuration (`.editorconfig`, `.globalconfig`) suppress it, `[SuppressMessage]` does not | Error, not suppressible | As under the current rules |
| Plain warnings (`RuleGroup.Plain`) | SCA1042, SCA1045, SCA1047, SCA1049 | Warning, suppressible in every way | The same | The same | The same |
| Unchecked assembly (`RuleGroup.UncheckedAssembly`) | SCA1025 | Warning in a project that references a package itself, message (Info) in one that gets the analyzer through another package | The same | The same | The same |

Under the updated rules the place of a report decides the severity of an enforced rule about a type that is unsafe to copy, not its ID. A report in code follows the unsafe
context of its place. A report at a declaration is the error unless the declaration is marked `unsafe` or `safe`: a declaration has no
unsafe context under the updated rules. Code is a body, an initializer of a field or a property, a constructor initializer, the
arguments of a primary constructor's base type, a lambda and a top-level statement; a local function and a lambda are declared in code,
so their parameters are code as well (`UnsafeContext.ContainsCode`). So SCA1009 is an error at a field and a warning at
`culture.Clone()` in an unsafe block, and SCA1022 is an error at the covariant return of an override, and a warning at one marked
`safe`.

The test for whether a rule is enforced is whether the person who gets the error can properly fix it in their own code. The four plain
warnings fail that test or are meant to be suppressed. SCA1042 is a line of a symbol list file, which has no unsafe context. SCA1045 is
an attribute named NonCopyable that is not the analyzer's, which a project can keep on purpose for another tool, and which breaks
nothing: the attribute is not recognized. SCA1047 is meant to be suppressed by the author of the method once it is checked, with
`#pragma` or `[SuppressMessage]`. SCA1049 is about how an attribute class is declared, as SCA1045 is, and is about no value: it says
what the analyzer reads, which is the same with the warning or without it. SCA1025 is reported for the whole compilation, with no place in code, and its fix is in someone
else's code; a project that only gets the analyzer through a library did not choose to vouch for the assemblies that it references.

A message is true under both rule sets. Most say nothing of the severity, and call a report a report. A message that advises a
suppression has a second text for the current rules, where a suppression can be anywhere (`Rules.CreateWithCurrentRulesMessage`). The
first text goes with the error and the warning of an unsafe context, so only a report about a type that is unsafe to copy gets it under
the updated rules: it advises an unsafe context for a report in code, and marking the declaration `safe` for a report at one. Where one
rule reported both in code and at a declaration, the declaration got a rule of its own, so that each text is true where it is used:
`CloningBindingInBaseList` for a binding in a base list, and `CloningUseReturn` for the covariant return of an override.

#### How a Severity Is Made

An enforced rule has three descriptors with the same ID: the ordinary warning of the current rules, with no tag of its own; the error of
the updated rules, with the custom tags `NotConfigurable` and `Compiler`; and the warning of an unsafe context, with the tag `Compiler`,
which is the descriptor of a report at a marked declaration as well. The error and the warning of an unsafe context each have a second
descriptor, with the same tags, whose message says after the rule's own that a type there is larger than the analyzer looks into and is
treated as if it could hold a value that is unsafe to copy (`Rule.ErrorAtBound`, `Rule.UnsafeWarningAtBound`, `Rules.AtBoundClause`). SCA1025 has a warning and an Info message, and a plain warning has one warning. All of them are in `SupportedDiagnostics`.
`NotConfigurable` keeps NoWarn, `.editorconfig` and rule sets off the error, and the compiler does not apply `#pragma` to an error.
`NotConfigurable` alone still lets `[SuppressMessage]` through: the `Compiler` tag is what makes Roslyn ignore that attribute, for the
error and for the warning of an unsafe context alike. A tag is read from the descriptor of the reported diagnostic, so the ordinary
warning can be configured and suppressed although another descriptor of its ID is not configurable; `SeverityTests` checks that. The
same holds for the warning of an unsafe context, which has no `NotConfigurable` tag: under the updated rules severity configuration, in
an `.editorconfig` or a `.globalconfig` file, turns it off as NoWarn does, and leaves the error as it is. So the ways to suppress that
warning are `#pragma`, NoWarn and severity configuration, and not `[SuppressMessage]`.

`Rules.cs` holds a `Rule` for each message, not a `DiagnosticDescriptor`, so nothing can report a rule with `Diagnostic.Create` by itself.
Every report goes through the `Severities` of the compilation (`DoNotCopyValueAnalyzer.Severities.cs`), made once at the start of the
compilation, which chooses the descriptor. It reads the MSBuild properties and the memory safety rules of the compilation once. Under
the updated rules it asks whether a report of an enforced rule is about a type that is unsafe to copy (see
[What a Report Is About](#what-a-report-is-about)), and gives a report about any other type the ordinary warning.

#### Unsafe to Copy

A non-copyable type is unsafe to copy by what can be read from its own declaration and its type arguments, and never by its fields
(`NonCopyableTypes.IsUnsafeToCopy`):

1. **A struct that is non-copyable by itself and says so:** the attribute that makes it non-copyable is given `UnsafeToCopy = true`,
   as the named argument or as an argument for a 'bool' constructor parameter named `unsafeToCopy`, or a line of a symbol list file or
   of the built-in framework symbol list marks it `unsafetocopy`. The attribute is the one that `IsNonCopyableMark` recognizes, read
   from source and from metadata, so a project's own internal copy of `NonCopyableAttribute` that declares the property counts the
   same. One without the property or the parameter cannot be given the argument, and its structs are plain non-copyable. On a struct of a referenced assembly an attribute of any namespace is recognized, as for the mark
   itself, and its `UnsafeToCopy` argument counts where it has one.
2. **A constructed struct with a type argument that is unsafe to copy,** of the type or of a type that contains it: `Nullable<T>`,
   a tuple, and any other generic struct that is non-copyable through that type argument. A generic struct is taken to store its type
   arguments inline, as far as anyone can tell from outside, and that is the rule that makes it non-copyable in the first place, so the
   same type arguments decide here. A view is the exception, as there. No field is looked at.
3. **A type parameter marked `[NonCopyable]` whose mark does not say `UnsafeToCopy = false`** (see
   [Type Parameters That Take No Type That Is Unsafe to Copy](#type-parameters-that-take-no-type-that-is-unsafe-to-copy)). Inside the
   generic it can stand for any non-copyable type.

A struct that a `copyable` line takes out is never unsafe to copy. The answer is worked out from the type arguments alone, and
remembered for the type as the answers of the other walks are (see [Walks Over Types](#walks-over-types)). What a container holds is a different walk: it
follows base types and interfaces as well, and those can mention the type itself, as `ImmutableArray<T> : IEquatable<ImmutableArray<T>>`
does, two generics can mention each other, and a generic can mention a deeper construction of itself, as
`C<T> : IEquatable<C<C<T>>>` does. That walk keeps a stack of its own, looks at each type once and stops at its bound with a strict
answer (`NonCopyableTypes.HoldsUnsafeToCopyInParts`; see [Walks Over Types](#walks-over-types)).

Working out a severity never stops a check. Where it fails for any reason, the report is strict (`Severities.IsAboutUnsafeToCopy`):
an exception that reached the code which reports would end the walk of the member, be taken for code nested too deeply (SCA1048), and
lose the other reports of the member.

**Why fields do not count.** Working it out from fields does not hold across assemblies: a reference assembly can replace the private
fields of a struct with placeholders, so the same library would be unsafe to copy when referenced one way and plain when referenced the
other. It is also unlike the rest of the analyzer, where a struct that holds a non-copyable field must be marked `[NonCopyable]` itself
and gets a report where it is not. So a struct that declares an instance field whose type is unsafe to copy must say
`UnsafeToCopy = true` on its own attribute, and the field gets SCA1017 in the struct's own build where it does not, the field that holds
a captured parameter of a primary constructor at the parameter. Until it says so, the struct is what its attribute says, and nothing
treats it as unsafe to copy on the side. A field whose type is unsafe to copy only through a type parameter of the struct, or of a
type that contains it, asks nothing (`IsUnsafeToCopyWithoutTypeParameters`): each constructed type of the struct is judged by its type
arguments. A struct with such a field that is not marked at all gets SCA1010, whose message then names `UnsafeToCopy = true`, so one
edit fixes both.

**Marked by itself.** A struct that holds a non-copyable field must be marked itself, whether or not it has marked type parameters. A
generic struct is non-copyable through a marked type parameter of its own, or of a type that contains it, and copyable where a
copyable type is bound to it: `Pair<int>` of `struct Pair<[NonCopyable] T> { T First; LockGuard Second; }` copies the guard. So
SCA1010 judges the struct and the type of the field with every type parameter counted as copyable (`IsNotMarkedFor`, through the
classifier of a `[RequiresCopyable]` member): the struct must be non-copyable by itself where the field is. A field of the type
parameter, or of a type over it, asks nothing, and the one report of a field whose type is unsafe to copy is the SCA1010 text that
names the property. A parameter of a primary constructor that the struct keeps is judged in the same way. A record and an
auto-property get their report whatever the type that declares them is, so a marked type parameter hides neither.

`UnsafeToCopy = true` on the mark of a type parameter is the default written out, and `UnsafeToCopy = false` on a struct is too. Neither
gets a report: the claim is true, and a warning would need an ID for a case that harms nothing.

The marker `unsafetocopy` of a symbol list line is `noncopyable` with a flag (`SafeListEntry.IsUnsafeToCopy`), so everything that reads a
non-copyable entry reads it alike. Two entries for a type merge to one that is unsafe to copy where either says so: for this claim the
stricter line is the one that says more, and nothing makes a type that is unsafe to copy plain non-copyable. A `copyable` line takes
such a type out completely, as it does any type.

Five of the eleven built-in types are `unsafetocopy`: the four GC handles and `MemoryHandle`. Each one's `Free` or `Dispose` clears only
its own field and frees what the old value named, with no check that it is still the same, and `GCHandle<T>.Target` reads the slot with
`Unsafe.As<T>`. The other six break the logic of the program. `DefaultInterpolatedStringHandler` is the closest call: a copy returns its
pooled array twice, which shares data but reaches no invalid memory. The reading of each is in the evidence file, beside the list.

#### What a Report Is About

Each enforced rule says what its reports are about (`Rule.About`), next to its message in `Rules.cs`, and `Severities` reads it. What
of a type counts depends on what the rule is about, and each rule says which of two it is (`About.IsCopy`):

- **A report that a value was copied** (`About.Copy`, `About.GivenCopy`) is about the values that the copy duplicates: the value
  itself and what it holds inline, through tuples, nullable types and struct fields by their type arguments, at any depth
  (`NonCopyableTypes.DuplicatesUnsafeToCopy`, which is `IsUnsafeToCopy` for a value). A reference that rides along, to a list, a
  delegate or any class, is not duplicated, so what is behind it does not count: a copy of `(PG, Func<UG>)` or of `(PG, List<UG>)` is
  the ordinary warning, as a copy of a `[NonCopyable]` struct with those two fields is. Copying a container is no copy of a value, so a
  container that such a rule names, as the rules of `[MovedFrom]` can, is about the container left in two places, where other code
  reaches it, and counts as for the second meaning.
- **A report that other code can get at a value or a container** (`About.Reach`, `About.GivenReach`): a member that is not on a list
  receives it, a conversion hides it, a method keeps it, a box hides what is inside. It is about everything that code can reach: the
  value, what it holds inline, and the values of the containers that it carries, at any depth (`NonCopyableTypes.ReachesUnsafeToCopy`,
  through `IsAboutUnsafeToCopy`). Passing `(PG, List<UG>)` to a member that is not on a list is strict. The box of a generic struct
  counts by what the struct is made of, its type arguments (`About.WhatArgumentHolds`), since its interfaces hand out boxed copies of
  those.

A severity reads a type in these two places only, so every rule follows one of them. The types are:

- **The arguments of the message** that are the types (the indexes of `About.Copy` and `About.Reach`): most reports name the
  non-copyable type.
- **A type that the code which reports gives** (`About.GivenCopy`, `About.GivenReach` and a `Subject`), where the message names no
  type or names it as text: a pattern part, a container in a text that names the container behind it, a mark of a parameter or a
  return value. A use of an object that hands it to code that can clone it (SCA1009) stands for the report of the conversion as well,
  so its subject is the binding that the clone copies and the converted value, which counts by what other code can reach of it
  (`Subject.OfCopyAndReach`).
- **No type** (`About.NoType`): the report is an error for any type.

| Meaning | IDs and rules |
|---|---|
| A copy | SCA1001, SCA1002, SCA1003 (a copy of a stored value, a value passed twice, combined delegates, a part used twice in a pattern, a `switch` that matches a copy, a use that is not known to make no copy), SCA1004 (the copy into a box), SCA1005, SCA1006, SCA1009 (the clone copies the binding), SCA1010 to SCA1017, SCA1021, SCA1030, and the rules of SCA1040 and SCA1041 about `[MovedFrom]` |
| Other code gets at it | SCA1020, SCA1022, SCA1023 (the box of a generic struct by what it holds), SCA1024, SCA1026 (generic code that takes only plain types copies the value or hands it to other code), SCA1031, SCA1032, SCA1033, SCA1043, SCA1048, the rules of SCA1040 about `[Kept]`, `[return: Borrowed]` and an inherited implementation that is not checked, and the rules of SCA1041 about a container moved out of a `[MovedFrom]` parameter |
| No type | SCA1007, SCA1008, SCA1044, SCA1046, and the rules of SCA1033, SCA1040 and SCA1041 about a type parameter that a caller binds |

A type parameter in the place of the type, an argument that is not a type, and a `Given` rule that was given no type all make the report
an error, so that a report that cannot be tied to a type stays strict. The exception is a type parameter whose mark says
`UnsafeToCopy = false`, which a caller binds only to a type through which no value that is unsafe to copy can be reached: a report about it, or about a declared type that
mentions only such type parameters, is the ordinary warning (`NonCopyableTypes.MentionsTypeParameterThatTakesUnsafeToCopy`).

**A report that involves several types** is strict where any of them is unsafe to copy, and where its message names one, it names
one that is unsafe to copy, by the meaning of the rule (`NonCopyableTypes.IsLessUnsafeToDuplicate` for a copy,
`IsLessUnsafeToReach` for the rest). Otherwise whichever came first in the source would decide, and a plain type ahead of the other
would make the report a warning that a `#pragma` removes. The places that choose one of several:

- The type argument of a member use (`ClassifyUse`), for each rule, and between a value and a container that the member is not
  vouched for, which are one rule that one line answers: `Pair<PG, List<UG>>` gets SCA1023 about `List<UG>`, not SCA1020 about `PG`.
  A type argument that the symbol list vouches for is not part of the situation: `new Dictionary<PG, UG>()` gets SCA1020 about `PG`
  as a warning, since the line covers `TValue`.
- The field of `MemberwiseClone`, of the class or of a class below it, the parameter of a member of unchecked code, the parameter of
  combined delegates, the container that variance hides, the holder that variance loses, the erasable base of SCA1043 and the
  awaitable of an `await foreach`.
- The binding of a class that clones itself, within one class and along its chain up to the class that clones itself
  (`PreferUnsafeToCopy`). A use that SCA1009 reports in place of SCA1023 is about what other code can reach of the value that it
  converts as well as about the binding that it names, so `Holder<Tagged<PG>, UG>` converted to `object` is strict.

**One report does not hide another.** Boxing a stored value copies it (SCA1004, about what the copy duplicates), and the box can also hide what the value holds, as
the box of a tuple, a struct of unchecked code or a value of a marked type parameter does (SCA1023, or SCA1026 for a binding that a type
parameter does not take). The two have different fixes, so both are reported, by a conversion, a conversion with no node, a pattern and a
`foreach` alike (`CheckWhatTheBoxHides`). A member use can break rules of different IDs for different type parameters, as SCA1020 for one
and SCA1022 for the constraint of another, and each is reported (`MemberUse.AlsoBroken`), once per type parameter: a later rule for a
type parameter that an earlier report names adds nothing that the fix of the first misses. And a member that the symbol list names
for some type parameters hands out values by its line also where the use breaks a rule for another (`MemberUse.ListedKind`): a
`foreach`, the indexer and `TryGetValue` of a stored `Dictionary<PG, UG>` get SCA1020 for the key and SCA1030 for the value, as with
`Dictionary<int, UG>`, where the first report used to stand for the whole use.

The same holds for `MemberwiseClone` in a class that another assembly can derive from (`CheckMemberwiseClone`). The call copies the
fields of the class and of its classes below in the compilation, which is SCA1005 about the field whose type is the most unsafe to copy,
and an object of a class of another assembly below it, which can hold a value of any type, which is SCA1008 with no type. Both are
reported. With SCA1005 alone where the class has a field, a plain field would make the report weaker than no field at all: SCA1008 is
an error under the updated rules, and SCA1005 about a plain type the ordinary warning. Fixing the field answers SCA1005 and not the
class of another assembly, so each report has a fix of its own, and an author who copies the fields in a constructor answers both.

These were not obvious, or stay strict for lack of a type:

| Rule | About | Meaning |
|---|---|---|
| A part used twice in a pattern (SCA1003) | The types of both parts: that of each variable, or of the receiver of each member that runs on a part. The outer part holds the inner one inline, so it is unsafe to copy where the inner one is | A copy |
| The containers of SCA1022, SCA1023, SCA1031, SCA1032 and SCA1033 whose message names the container with the container behind it | The container, given by the code that reports | Other code gets at it |
| The variance rules, SCA1022 and SCA1023 | The container that the conversion hides | Other code gets at it |
| `HolderLostByVariance` (SCA1031) | The holder and the container | Other code gets at it |
| SCA1009, all its texts | The non-copyable type that is bound to the class that clones itself, or that its field holds | A copy; the converted value of a use, by what other code reaches |
| `MemberwiseCloneOfTypeParameterThroughListedBase` (SCA1009) | The type of the field, which mentions a marked type parameter, so it is strict unless the type parameter says `UnsafeToCopy = false` | A copy |
| `ErasableBaseNotKept` (SCA1043) | The erasable type, as a container | Other code gets at it |
| SCA1024 | The type of the parameter through which the member gets at the value | Other code gets at it |
| `[MovedFrom]`, `[Kept]` and `[return: Borrowed]` on a parameter or a return value (SCA1040, SCA1041) | The type of the parameter or the return value; strict where it mentions a type parameter, marked or not, since a caller binds what that stands for, unless the type parameter says `UnsafeToCopy = false` | A copy for `[MovedFrom]`, where a container counts by what other code reaches; other code gets at it for `[Kept]` and `[return: Borrowed]` |
| SCA1017 | The type of the field | A copy |
| SCA1026 | The type argument; a type parameter there counts as unsafe to copy, since the report is only made for one that does not say `UnsafeToCopy = false` | Other code gets at it |
| `KeepingNotOnInterfaceMember` and `KeepingNotOnDelegate` (SCA1040) | The type of the parameter of the interface member or the delegate, which is the container; the inherited member's own is a type parameter | Other code gets at it |
| `[MovedFrom]` on something that is not a parameter (SCA1041) | The type of the field, the property, the event or the return value that carries it | A copy, where a container counts by what other code reaches |
| `OpaqueTypeParameterConversion` (SCA1023), `DelegateOverTypeParameter` (SCA1031) | The type parameter, and the return type that mentions it: strict unless the type parameter says `UnsafeToCopy = false`, since a caller binds what it stands for, a container of a type that is unsafe to copy included, and a type parameter that says `false` takes no such container | Other code gets at it |
| `SlotGivenToOwner` (SCA1033), the generic moves (SCA1041) | No type: whatever a caller binds | No type |
| The marks of type parameters, `UnsafeToCopy = false` on an override or an implementation, and `[RequiresCopyable]` (SCA1040) | No type: whatever a caller binds | No type |
| `MemberwiseCloneOfOpenClass` and `MemberClonesWithoutLine` (SCA1008) | No type: what another assembly derives or binds | No type |
| `GetObjectValueCall` (SCA1007) | No type: every call of `RuntimeHelpers.GetObjectValue` | No type |
| SCA1044, SCA1046 | No type: a conditional attribute of the analyzer, a `[NotNullRef]` that does not count | No type |
| SCA1048 | A value of the member's code that is unsafe to copy, given by a walk over the member (`FindValueUnsafeToCopy`); strict where the walk finds one or gives no answer | Other code gets at it: the member was not checked for any rule |

**A member nested too deeply.** SCA1048 says that a member was not checked, so it has no type of its own. It has two texts: one for
an expression that is nested too deeply, and one for a type with more parts than a walk looks at (`Rules.TypeTooLarge`), which a
declaration can get as well. At a declaration it is strict, as a report that cannot be tied to a type. Strict for every such member, it
would be an error at a long chain of string concatenations with no non-copyable type in sight, and in generated code where nobody can add
an unsafe context: the person who sees it could not answer for it. So it is strict only where the member's code involves a value that is
unsafe to copy, by the analyzer's own definition, a container that holds one and a marked type parameter that does not say
`UnsafeToCopy = false` included. The walk goes over the operations of the member with a stack of its own, so it cannot run out of stack
however deep the code is, and looks at the type of each operation, at the signature of each symbol that an operation names (a member, a
local, a lambda, a local function, an operator) and at the type that declares a member that is used, at the parameters and the result of
the member, and at the struct of an instance member of one. It also counts what a rule with no type would report, since the value that
such a rule is about can be of any type (`IsAboutAnyType`): a call of `MemberwiseClone`, which copies the fields of a class below the
one that the walk sees, a call of `RuntimeHelpers.GetObjectValue`, a call of a member that a `clones` line names, a value of a marked
type parameter converted to a type that hides what is bound to it, which can be a container of a type that is unsafe to copy, a delegate
made from a function over a marked type parameter or with a `[MovedFrom]` parameter, and the body of a generic move. A type parameter that
says `UnsafeToCopy = false` takes no such container, so its value hidden and a delegate over it count as what they are. It runs after the handler of the exception that stopped the check has left the frames that
ran out of stack: a catch block runs on top of them. Where the walk stops for any reason it gives no answer, and the report stays
strict. Under the current rules nothing changes.

The other reports with no type stay strict on purpose:

- **SCA1007:** the value that `RuntimeHelpers.GetObjectValue` is given can be of any type.
- **SCA1008:** the class that derives from the library's class, or the type that is bound to it, can be of any type.
- **SCA1044 and SCA1046:** the fix is always one edit, taking off `[Conditional]` or moving the `[NotNullRef]`.

#### The Unsafe Context

Only the updated rules ask for it. The context is that of the syntax node at the place of the report, and follows the C# language
(`UnsafeContext.cs`):

- An `unsafe` block and an `unsafe(expr)` expression are unsafe. The compiler that the analyzer is built against has no `unsafe(expr)`,
  so its kind is looked up by name (`UnsafeExpression`) in the compiler that runs the analyzer, never by a reference that the build
  would need.
- The `: base(...)` or `: this(...)` initializer of an `unsafe` constructor is unsafe. No other modifier makes anything unsafe.
- From C# 13 on the body of an iterator is safe, so a local function that is an iterator is safe inside an unsafe block, and an `unsafe`
  block in it is unsafe again.

Which rules a compilation uses is read from `IModuleSymbol.MemorySafetyRulesVersion` of its source module, by reflection, since the
property is newer than the compiler that the analyzer is built against. A compiler without it, which may still know the updated rules,
has only the feature flag `updated-memory-safety-rules` of the parse options. `CSharpCompilationOptions.MemorySafetyRulesVersion` is not
read: it was seen to stay 1 where the module's version was 2. The compiler that the analyzer is built against (5.0.0) does not know the
updated rules: it accepts the feature flag and goes on with the current rules. So the flag counts only in a compiler that has the
`unsafe(expr)` expression, which came with the updated rules (it is missing in 5.0.0 and present from the 5.9 compiler of the later
.NET 10 SDKs on), and in a compiler without it the analyzer applies the current rules as the compiler does.

The rules were checked against the compiler of the .NET 11 SDK, place by place; `SeverityTests` holds that matrix.

#### Marked Declarations

Only the updated rules ask for it. A report at a declaration gets the warning of an unsafe context where the declaration that it is at
has the modifier `unsafe` or `safe` (`UnsafeContext.IsAtMarkedDeclaration`). The declaration is found from the node at the report,
outward, and the first code or declaration on the way decides:

- A member: a field, a method, a property, an indexer, an event, a constructor, a destructor, an operator or a delegate. A part of a
  member counts as the member: a parameter, the return type, a type parameter, an attribute and an accessor. The compiler rejects the
  same modifier on every accessor of a property (CS9397) and asks for it on the property, so the modifier of an accessor is not read.
- A type, for a report at its name, its base list, a type parameter or a parameter of its primary constructor. A positional property
  of a record and the field that holds a captured parameter are reported at the parameter, so at the type: neither has a declaration
  of its own.
- A local function, for a report at its signature, which is code as well, so an `unsafe` block around it still counts. Its body is code.
- Neither the type around a member nor the member around a local function counts. A node in code is reached through a body, an
  initializer or a lambda before any declaration, and is at none.

What the compiler of the .NET 11 SDK (5.12) accepts:

| Modifier | Accepted | Rejected |
|---|---|---|
| `unsafe` | Fields, methods, properties, indexers, events, constructors, operators, interface members, local functions. Every use of such a member needs an unsafe context (CS9362), and the modifier needs `AllowUnsafeBlocks` (CS0227) | A type, a delegate, a destructor, a static constructor (CS9377); a member that overrides or implements one that is not `unsafe` (CS9364, CS9365, CS9366) |
| `safe` | Every member and type above, delegates and local functions included, without `AllowUnsafeBlocks` | A constant, an enum, an `extension` block (CS0106); an enum member and a parameter (a syntax error) |

So the receiver parameter of an `extension` block stays an error: the block takes no modifier, and the static class around it does not
count. A constant and an enum member cannot be marked either, but no rule reports at one.

`safe` counts only in a compilation that allows unsafe code (`Severities.SafeModifierCounts`): answering for a report takes the
project's choice to allow unsafe code, whatever a given compiler requires for `safe`. This condition is not settled, and is kept in that
one place so that it can be dropped. `unsafe` needs no check, since the compiler accepts it only in such a project.

The compiler that has the modifier parses `safe` as a contextual keyword of kind `SafeKeyword` in the modifier list of the declaration.
The compiler that the analyzer is built against has no such kind, and reads `safe` there as the name of a type. So the kind is looked
up by name, as `UnsafeExpression` is, and a compiler without it never has the modifier. In the preview version of the language the newer
compiler reads `safe` before a member's type as the modifier, under the current rules as well, so a type named `safe` is written
`@safe` there.

#### Where a Report in Code Is Placed

Under the updated rules a report in code is covered by an `unsafe(expr)` expression only where the report lands inside the expression.
So a copy is reported at the expression that makes it, not at a declaration, a designation or a keyword:

- The copy into a declared local is reported at its value (`var copy = _guard;` at `_guard`), not at `= _guard`: an `unsafe` block
  around the declaration would end the scope of the local.
- What a `foreach` does with each element is reported at the collection: the copy into the variable, the copy before a `Deconstruct`
  method, the conversion to the type of the variable, and the variables of a deconstruction. The alternative was an `unsafe` block
  around the whole loop, its body included.
- The variables of a deconstruction that are given an element of a value are reported at the value (`var (a, b) = _pair;` at `_pair`):
  a deconstruction declaration cannot be in an expression, and an `unsafe` block would end the scope of its variables.
- What a spread does with each element is reported at its operand, not at `..`.

These were looked at and left where they are:

- The parts of a pattern, a pattern variable among them, in an `is` expression and a `switch` expression: the whole expression can be
  in `unsafe(...)`, and a pattern variable declared there stays in scope after it. In a `switch` statement, an `unsafe` block around the
  statement holds the case, and its variables are scoped to the section anyway.
- An `await`: `unsafe(await task)` holds it, and the updated rules allow an `await` in an unsafe context.
- A member of an anonymous type: the creation expression can be in `unsafe(...)`.
- A parameter of a lambda or of a local function, as SCA1012 reports for an iterator: the lambda can be in `unsafe(...)`, and the local
  function in an `unsafe` block.

The `Dispose` or `DisposeAsync` method that a `using` calls (SCA1020, SCA1024 and the other reports of a member that is used) is
reported at the value of the resource, as a copy is. For a declaration, `using var pool = CreatePool();` or `await using var`, and for
a statement that declares its variables, `using (var pool = CreatePool())`, that is the initializer of each variable, so
`using var pool = unsafe(CreatePool());` covers it: an `unsafe` block around the declaration would end the scope of the variable. For
`using (expression)` it is the expression, as it was. `ReportPlaceTests` holds each form under both rule sets.

#### The Way Out and the Marker

The MSBuild property `NonCopyableErrorsAsWarnings` makes the analyzer report as it does under the current rules: every enforced rule
is the ordinary warning, with the message of the current rules, every suppression works (`#pragma`, NoWarn, `[SuppressMessage]` and
severity configuration), at a declaration as well, and whether code is in an unsafe context plays no part. So under the current rules
it changes nothing at all. It is for a project that has to get past a defect of the analyzer, or that prefers the warnings.

The build does not tie the marker of an analyzed build to the property: a project gets the marker whether the property is on or not
(see [Code Not Built With the Analyzer](#code-not-built-with-the-analyzer)). The property is only a preference for how reports appear
and how they are suppressed. The author still gets every report, so the assembly is as checked as one built under the current rules,
which has the marker too.

Whether the project references `Singulink.CodeAnalysis` or `Singulink.CodeAnalysis.Analyzers` itself is worked out by the targets file
into `_NonCopyableReferencesPackage`, always, whatever the project sets for the built-in types, and the props file makes it visible to
the compiler. A build without it, such as a test, counts as one that references a package.

#### Testing the Severities

The tests run under the current rules, and expect each result as the ordinary warning. A test under the updated rules runs through
`Verify.UpdatedRulesAsync`, which turns the feature flag on with the preview version of the language and is inconclusive in a compiler
without the updated rules, and expects an error with `AsError`. Two things of the testing library needed changing in `Verify.Test`. It
takes a diagnostic with the `Compiler` tag for one of the compiler, and leaves it out by the level of compiler diagnostics that the test
checks; `IsCompilerDiagnosticIncluded` takes every diagnostic of the analyzer in. And it runs each test again under a
`#pragma warning disable` line, taking a diagnostic to be suppressible or not by its ID: not where any descriptor of the ID is not
configurable, which holds for every enforced ID. That run is turned off (`SkipSuppressionCheck`), and `SeverityTests` checks what
`#pragma`, `[SuppressMessage]`, NoWarn and severity configuration do under both rule sets, and with `NonCopyableErrorsAsWarnings`
under the updated rules, where one test compares every report with the one of the current rules: place, severity, tags and message.
A second matrix holds a report at each kind of declaration, run with no modifier, with `safe`, and with `unsafe` where the compiler
accepts it, then with `safe` in a project that does not allow unsafe code, and under the current rules.

A test that expects an error under the updated rules uses a type that is unsafe to copy: `Sources.UnsafeGuard`, or a source of its own
through `Sources.AsUnsafeToCopy`. Each such test has a mirror with a plain non-copyable type, which expects the ordinary warning under
the updated rules and every suppression to work on it. `UnsafeToCopyTests` holds how a type is found to be unsafe to copy, the merge of
the list markers, containers, messages and `NonCopyableErrorsAsWarnings` with both kinds of type.

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
| Working out from its fields whether a struct is unsafe to copy | A reference assembly can replace the private fields of a struct with placeholders, so a library would be unsafe to copy when referenced one way and plain when referenced the other. Replaced by the struct saying so itself, with SCA1017 for one that holds such a field and does not; see [Unsafe to Copy](#unsafe-to-copy) |
| A marked type parameter that is always unsafe to copy | A generic meant for plain non-copyable types would answer for every copy with an unsafe context. Replaced by three forms of the mark; see [Type Parameters That Take No Type That Is Unsafe to Copy](#type-parameters-that-take-no-type-that-is-unsafe-to-copy) |
| An MSBuild property to mark a type or take one out | The symbol list is the one place for both |
| The outcome `throws` of `borrowswhen`, which let a `catch` use a value that a member had not taken where it threw | It was built and reviewed, and left out for two reasons. What a member may throw, and when, has too much nuance to expect people to classify members accurately, even with a careful review of the source: classifying the framework's own took several passes and needed a rule for which cases count. And the analyzer can never check whether a member keeps a value where it throws, since nearly anything can throw, so the outcome would always rest on manual marking. `true` and `false` differ there: a later checked attribute can verify them against the body of a method. A later analyzer can add an outcome word without breaking a symbol list file, since an unknown outcome word is skipped |

## Warnings on Correct Code That Were Accepted

The analyzer follows a value through one method at a time and assumes that a value passed by value is gone. Some correct code still
gets a warning. These were accepted:

| Shape | Why it stays | Where it is told |
|---|---|---|
| An `async` helper that fills a container which its caller awaits | The helper keeps running after its first `await`, and nothing shows that the caller waits for it | [Correct Code That Still Gets a Warning](Docs/articles/guides/correct-code-with-warnings.md) |
| Handing a value over and keeping it on failure, through a member with no `borrowswhen` marker, or with a `catch` or a `finally` that uses the value (`ChannelWriter<T>.WriteAsync`) | What the analyzer sees is a value passed by value and used again, which is reported at the use after the hand-off (SCA1016). A member that throws may have taken the value, so a `catch` is reported after a member with the marker too | [Correct Code That Still Gets a Warning](Docs/articles/guides/correct-code-with-warnings.md) |
| A race between two tasks that hold a guard | The same | The same |
| `ManualResetValueTaskSourceCore<T>.GetResult` in a value task source | `GetResult` leaves the result in the core until `Reset`, so the warning is accurate; it is suppressed there | [Value Task Sources](Docs/articles/rules/listed-members.md#value-task-sources) |
| A lambda of an expression tree that keeps its parameter (SCA1032) | The lambda is not made into a delegate where it stands, so nothing says what its callers take, and it is judged as a method is. A tree that is compiled and run that way is rare | [Lambdas](Docs/articles/rules/keeping-containers.md#lambdas) |
| A `switch` statement over a stored local, in an optimized build | The compiler copies the local only in a build that is not optimized, or with a `when`; the analyzer does not follow the configuration | [Read-only References and Temporary Copies](Docs/articles/rules/read-only-references.md) |
| An `in` argument that is not a variable, over a stored value | The callee holds a second instance for the call | [Read-only References and Temporary Copies](Docs/articles/rules/read-only-references.md) |
| A union written by hand with `[Union]`, matched through `TryGetValue` | Judged as a box like every union: strict, and safe, since such a union is rare and its `Value` boxes a copy anyway | [Boxes and Unions](Docs/articles/rules/boxes.md) |
| A generic that names a deeper construction of itself in its base list, or two that do it through each other (`C<T> : IEquatable<C<C<T>>>`) | The walks over its base types reach their bound and take the answer that leads to a report: a conversion of `C<PG>` to its own interface gets SCA1023, and under the updated rules a report about `C<PG>` is strict | [Generic That Expands Itself](Docs/articles/guides/correct-code-with-warnings.md#generic-that-expands-itself) |
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
- **The lock object of a key or a value collection of `Dictionary<,>`.** On .NET, `ICollection.SyncRoot` of both is the dictionary
  itself, so a cast of it back to the dictionary reaches the other half of every entry. The `erasable;for=` lines stay: the route takes a
  cast to a legacy interface and a property that is documented as an object to lock on, which is an API used against its documentation.
  The evidence file says exactly what the interfaces hand out.
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
| `DoNotCopyValueAnalyzer.Flow.cs`, `.FunctionGraph.cs`, `.LastUse.cs` | The paths through a function: the last use, the branch of a `borrowswhen` member that takes the value, the commit, and the path checks of `[MovedFrom]` |
| `DoNotCopyValueAnalyzer.HandOffs.cs` | The check of a member, and the report of a copy that a hand-off made at the first uses after it |
| `DoNotCopyValueAnalyzer.WhatRuns.cs`, `.KnownReferences.cs` | What runs no other code and cannot throw, and which references are known to refer to something |
| `DoNotCopyValueAnalyzer.Declarations.cs` | Checks of declarations: fields, structs, attribute classes, the marks of types and members, erasable bases, covariant returns |
| `DoNotCopyValueAnalyzer.Assemblies.cs`, `AnalyzedAssemblies.cs` | The marker of an analyzed build, and SCA1025 |
| `NonCopyableTypes.cs` | Which types are non-copyable, containers, views or erasable, and which member uses are safe. A compilation has two instances: one for checked code, and one for the bodies of `[RequiresCopyable]` members, in which marked type parameters count as copyable |
| `SafeMemberList.cs` | The symbol list: the parser, the merge, and the built-in framework symbol list from the embedded `NonCopyableSymbols.txt` |
| `CarriedSymbols.cs` | What a container parameter may be used in, and what each of those operations carries |
| `DiagnosticIds.cs`, `Rules.cs`, `Rule.cs` | The IDs, the rules and their descriptors, the group of each ID, and what the reports of each rule are about |
| `DoNotCopyValueAnalyzer.Severities.cs`, `UnsafeContext.cs` | The choice of the descriptor of each report, and the unsafe context or the marked declaration of a place (see [Errors and Warnings](#errors-and-warnings)) |

The rules for values come from Roslyn's `RS0042` rule (`DoNotCopyValue`), and `Acquire` in `NewValues.cs` keeps Roslyn's name. It
returns `RefKind.None` for a new value, and any other `RefKind` for a stored one. How this analyzer differs from RS0042 is in
[Differences From Roslyn's Rule](Docs/articles/rules/roslyn-differences.md).

#### Walks Over Types

Every walk over types must end, and quickly, for every type that the compiler accepts. A visited set is not enough. A generic can name a
deeper construction of itself in its base list, as `class C<T> : IEquatable<C<C<T>>>` does, or two can expand each other, as
`A<T> : I<B<A<T>>>` and `B<T> : I<A<B<T>>>` do, so a walk that follows base types and interfaces into their type arguments meets a new
type at every step. A delegate can take a deeper construction of itself, as `delegate void D<T>(D<D<T>> d)` does. A type that the
compiler infers through a chain of calls can be nested thousands of levels deep, and a tuple of tuples of the same tuple has more paths
through its parts than parts. Such shapes once ran the compiler for ever, or ended its process with a stack overflow.

**A count of the steps does not bound the work.** With `class X<T> : IEquatable<X<(T, T)>>`, or `X<Func<T, T>>`, or
`X<T, U> : IEquatable<X<U, X<T, U>>>`, each step of the base list has twice the parts of the one before. The compiler shares the parts
that repeat, so making such a type is cheap, and the compiler alone builds such code in two seconds. Comparing two of them that are equal
and were made apart is not cheap: the compiler compares them part by part along every path (`NamedTypeSymbol.EqualsComplicatedCases`), so
the fortieth costs 2^40 steps. A visited set and a cache compare what they hold, and a walk that counted only its steps, 512 of them, let
such types in and ran for more than ten minutes: the source and the target of `IEnumerable<X<PG>> e = new List<X<PG>>()` each make their own chain of
equal types, and every lookup of one in a set or a cache that holds the other compares them in full.

So every walk has one budget, a `TypeWalk`, and looks at each type through it (`LookAt`), the type that it starts from, each part and each
base type, interface, constraint or type of a signature that it takes up alike. The budget counts the types that the walk looks at, at most
`TypeWalk.MaxTypes` (512), and counts the parts of each type before the walk looks at it, with a count that stops at
`TypeWalk.MaxPartsPerType` (512), so it never costs more than that. A part is the type itself, a type argument of it or of a type that
contains it, an element type, what a pointer points at and the types of a function pointer's signature, each time that it is met: a type
nested 513 levels deep has too many parts, and so has a type that doubles its parts nine times. A type within the limit is compared with
any other type in no more steps than its parts, so one walk costs at most 512 times 512 counted parts and a bounded number of comparisons,
whatever the shape of the types. A walk that only follows constructors or the constraints of type parameters counts its steps (`Step`).
One class holds both limits and the count, and a walk cannot look at a type in any other way, so a new walk cannot leave the budget out.

**A cache keys a type by its identity** (`TypeIdentities`, `TypeCache`): the classification of types, the erasable types, the bindings of
cloning classes, the classes that derive from a listed container, and the answers of the walks below. The compiler makes a type anew
wherever it is written or substituted, hashes every construction of a generic as its definition, and compares two that are equal and
were made apart part by part. So a cache keyed by the type puts every construction of one generic in one bucket, and compares a type
nested hundreds of levels deep with the copy that another field left, at each level that a walk asks about: a cost that grew with the
square of the depth at every use. The identity of a type is made from that of its definition and the identities of its parts, from the
innermost out, and kept for the instance of the type, so each instance is looked at once, and each lookup after that compares nothing.
Nullable annotations and the names of tuple elements play no part, as in `SymbolEqualityComparer.Default`. A type with more parts than a
walk looks at is its own identity, by its instance, so that making an identity never goes deeper than a walk counts. The count and a
hash of the parts of each instance are kept as well (`TypeWalk.Measure`, in a table that holds the symbols weakly), so counting the
parts of a type nested 400 levels deep costs one step per level once, and not once for each of its levels at each look of a walk. The
sets of a walk hash a type by its parts for the same reason (`TypeWalk.Comparer`), and the uses of members by the parts of the types
that they are constructed with (`TypeWalk.MemberComparer`).

**A walk that starts from a type is the same walk each time,** and the checks of a member ask the same questions at every use of a type:
whether a conversion loses sight of a non-copyable type (`GetMentionedNonCopyableTypes`), whether a type exposes one, whether it is
unsafe to copy, whether its parts hold such a value, and the binding of a cloning class within it. Each answer is remembered by the type
that the walk started from (`NonCopyableTypes.Remember`), the answer that the walk reached its bound included (`WalkAnswer.AtBound`), so a
type that expands itself or has hundreds of parts costs its walk once per compilation and not once per use. Five thousand conversions of
`List<C<PG>>` to `IEnumerable<C<PG>>`, with `C<T> : IEquatable<C<C<T>>>`, and a thousand fields of dictionaries of lists nested 505 levels
deep, three uses each, build within about twice the time of the compiler alone under both rule sets.

A dictionary of lists of tasks of tuples comes to about fifty types, and a walk that reaches its bound ends in a few milliseconds. Every
generic of the shapes above, whether it grows by a tuple, a delegate, two type parameters, a base class, a constraint, a nested type or a
field, builds within a fraction of a second of the compiler alone, under both rule sets. What a walk answers at its bound depends on what
it is for:

- **A walk that follows base types, interfaces or delegate signatures** is the one that the shapes above reach. At its bound it takes the
  answer that leads to a report: a severity is strict, and a type carries a non-copyable value. A conversion of `C<PG>` to its own
  interface then gets SCA1023, and a report about `C<PG>` is strict under the updated rules, where the walk would have found only `PG`.
  The shapes are rare, and a report on them is the price of an answer.
- **A walk over the parts of a type alone** ends by itself, and reaches the bound only with a type of more than 512 parts. Where its
  answer is a yes or a no, it takes the one that leads to a report. Where its answer has to name what it found, such as the binding that
  SCA1026 names, it gives up with a `TypeTooLargeException`, and the member or the declaration gets SCA1048 with the text of a type that
  is too large (`Rules.TypeTooLarge`), as a recursion that runs out of stack gives up. In the code that works out a severity it makes the
  report strict. Each walk over the type of a field is made by one check of that field: the field's own (`AnalyzeField`), or for a
  field that the compiler declares, behind an auto-property or for a parameter of a primary constructor, a check of its own inside the
  check of its struct (`AnalyzeFieldDeclaredByCompiler`). So a field of a type that is too large gets SCA1048 once, where the field is
  shown, and the rest of its struct is checked.
- **`GetKind`** follows base types and interfaces only for a definition, so it looks only at the parts of a type and at the base lists
  of the definitions that it meets, and ends for every type. Its walk has the same budget: a type that the walk does not look at carries
  something as far as the analyzer can tell, so a struct there is non-copyable and anything else a container, and that answer is not
  cached, for the type or for one that it is a part of.

| Walk | What it follows | At the bound |
|---|---|---|
| `CollectNonCopyableTypes`, for `LosesNonCopyableType` | Parts, base types, interfaces | The conversion loses sight of a non-copyable type |
| `ExposesNonCopyableType` | Parts, base types, interfaces | The type exposes a non-copyable type |
| `HoldsUnsafeToCopyInParts` | Parts, and the base types and interfaces of a container | The type holds a value that is unsafe to copy: strict |
| `HoldsContainers`, `MentionsContainer` | Parts, base types, interfaces | The type holds containers, so a local of it is held to the list of what is allowed |
| `IsElementTypeOf` | Parts, base types, interfaces | No element: what a member hands out is the container under another name |
| `FindHiddenByVariance`, `FindHolderLostByVariance` | Variant type arguments, and the interfaces of the source | The conversion itself hides a container, or loses a holder |
| `FindHandBackType` | The types of a signature, delegate signatures, constraints, each part once each way: a type argument of a class goes both ways, and without a record of what it saw the walk doubled its work at each level of a dictionary of dictionaries | The member can hand its object on |
| `LetsOut`, for SCA1025 | Pointers, arrays, the signatures of delegates and function pointers | The member lets a value out |
| `MentionsBoundContainer` | Parts | A bound container: what the member hands out is stored |
| `FindVariance` | Variant type arguments of an interface | An obstacle at the type argument that the walk got to: the type is not erasable |
| `CoversEveryTypeParameter` | Parts of a declared type | Not covered: the value counts as taken |
| `HasContainerConstraint`, `GetClassOf`, `GetConstraintClass` | Constraints to type parameters, counted by step | Nothing found: only a circle reaches the bound, and that is a declaration that does not compile |
| `IsUnsafeToCopy`, `FindBindingNotTakenWithin`, `FindStructBindingNotTaken`, `FindCloningBindingWithin`, `MentionsTypeParameter`, `MentionsMarkedTypeParameter`, `MentionsUnmarkedTypeParameter`, `Substitute`, `FindConstructorBindingNotTaken`, `FindRequiresCopyableConstructorUse` | Parts, or the constructors of the classes above, counted by step | `TypeTooLargeException`: SCA1048, or a strict severity |
| `GetKind` | Parts, and the base lists of definitions | A struct is non-copyable, anything else a container, and nothing is cached |

**A report that a bound made strict says so.** The walks that decide a severity give a third answer besides yes and no, that they
reached their bound (`WalkAnswer.AtBound`): `HoldsUnsafeToCopyInParts` and `IsUnsafeToCopy`, and through them `AnswerReachesUnsafeToCopy`,
`AnswerDuplicatesUnsafeToCopy` and `AnswerHoldsUnsafeToCopy`, which `Severities` reads, a `TypeTooLargeException` there included. A
report that is strict only by that answer gets the descriptor whose message says so; one that a type which holds a value that is unsafe
to copy makes strict needs no such words, whatever else reached the bound. A list of lists twenty levels deep over a plain guard meets
more than 512 types along its interfaces, and a tuple of 520 elements has more than 512 parts: both make a report strict that way. The
member that SCA1048 reports and a declaration that was not checked for a type that is too large say it as well: the walk that looks for a
value that is unsafe to copy in the member takes a type that reached the bound where it finds nothing else, and a declaration stopped by
a `TypeTooLargeException` gets `Subject.TooLarge`. Under the current rules the severity reads no type, and nothing is added.

What is not carried to a message is a report that the bound decided, as opposed to its severity: a conversion that counts as hiding a
non-copyable type because `CollectNonCopyableTypes`, `ExposesNonCopyableType` or `FindHiddenByVariance` reached the bound, as for `C<PG>`
converted to its own interface, and a member use whose classification met the bound in `GetKind`. Those answers are yes or no to
`HidesNonCopyableType`, which seventeen places read and turn into a dozen rules, and to `GetKind`, which more than two hundred read;
carrying a third answer through them would touch most checks, and each rule would need a second text under the current rules as well,
where no severity is chosen by a type. Under the updated rules the severity of such a report reaches the same bound in
most cases, and then its message says so; under the current rules it says nothing of the bound.

A walk that once stopped at a depth of 8 or 16 with no answer, as the one of SCA1026 through the type arguments of a struct did, counts
types now: a binding nine structs deep is reported.

**What a message shows.** The compiler formats a type by recursion over its type arguments, with no guard, so formatting a type nested
thousands of levels deep ends the process. Every symbol that the analyzer shows goes through `SymbolText`: the text of a member, the
symbols among the arguments of a message (`Severities` replaces them), and the texts that the analyzer builds. A type nested more than
32 levels deep, or with more than 256 parts, is shown with four levels and `...` for the rest, and a member that mentions one is shown
as it is declared.

#### Messages

Every message is a `Rule` in `Rules.cs`, made by `Create(id, title, message, description)`. The title, the message and the description
are constants at the top of the file. One ID can have several rules, each with a message for one case, and they share the ID's title.
Each rule has a descriptor for every severity that its group can report it with (see [Errors and Warnings](#errors-and-warnings)). All
descriptors are in the category Reliability, and the help link of each is the address of its article:
`https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/diagnostics/` followed by the ID in lower case and `.html`. That address
is fixed from the first release on.

`Rules.All` lists every rule, and `Rules.Descriptors` every descriptor of them, which is what the analyzer supports. A test fails if a
rule is missing from it.

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
| `HandOffTests` | Where a copy that a hand-off made is reported: at the first uses after it (SCA1016), and what stays at the read |
| `BorrowsWhenTests` | The `borrowswhen` marker: the forms of a condition that are followed, the branch where the value was not taken, a `catch` and a `finally` after the hand-off, which are reported, the lines that the parser takes and reports, an outcome that the analyzer does not know, the merge of two entries, and the built-in entries that have the marker |
| `MoveTests` | `[MovedFrom]` and `NonCopyable.Move` |
| `TypeParameterTests`, `UnmarkedGenericTests`, `GenericStructTests` | Marked and unmarked generics, `[RequiresCopyable]`, kept marks, and generic structs and views |
| `ListedMemberTests`, `OwnedTaskTests`, `ReturnedContainerTests`, `ContainerOwnershipTests`, `OwnershipListTests`, `ConversionTests` | Containers: what a listed member hands out, returning, keeping, owning, moving and converting them |
| `CarriedSymbolTests` | The list of `CarriedSymbols.cs` against the compiler's API, by reflection |
| `RecordAndCloneTests`, `BuiltInTypeTests`, `AttributeTests` | Records and clones, the eleven built-in types and their property, and how attributes are recognized |
| `UncheckedCodeTests`, `UncheckedAssemblyTests` | Code not built with the analyzer: the marker, SCA1024 and SCA1025 |
| `SeverityTests` | Errors and warnings: the ordinary warning of the current rules, the unsafe contexts and the marked declarations of the updated rules, what `#pragma`, `[SuppressMessage]`, NoWarn and configuration do under each, NonCopyableErrorsAsWarnings, and SCA1025 as a message |
| `UnsafeToCopyTests` | The types that are unsafe to copy: the attribute in source, in metadata and declared by the project, a field that asks the struct to say so (SCA1017), constructed types by their type arguments, the three forms of the mark of a type parameter, the `unsafetocopy` marker and its merge, containers, the messages of the updated rules, and NonCopyableErrorsAsWarnings |
| `UnsafeToCopyReportTests` | The severity where working it out is not simple: a self-referencing or mutually recursive generic, a report that involves more than one non-copyable type, and a report whose value can be of any type |
| `UnsafeToCopyTypeParameterTests` | A type parameter marked `UnsafeToCopy = false`: the severity inside the generic, every way of binding a type to it (SCA1026), a type parameter of the generic around, the mark read from metadata, and overrides, implementations and partial methods (SCA1040) |
| `NonCopyableAttributeTests` | The attribute class itself: the getter of `UnsafeToCopy` that throws where it was not set, and `GetUnsafeToCopy()` |
| `ReportPlaceTests` | That a copy in code, and the `Dispose` that a `using` calls, is reported at the expression that makes it, which `unsafe(expr)` can hold |
| `SafeListTests` | The symbol list format, the merge, the built-in framework symbol list, and the diagnostics' help links and articles |
| `FrameworkScanTests` | The built-in framework symbol list against the shared frameworks, and against the .NET Framework 4.8.1 where it is installed (see [Symbol List](#symbol-list)) |
| `MessageTests` | The parts of messages that say why, and the fixes that the README and the guides give |
| `DeepCodeTests`, `EdgeCaseTests` | Deep and long code, on a stack of a fixed size, and the paths that only unusual code reaches |
| `TypeWalkBoundTests` | The bound of the walks over types: generics and delegates that expand themselves or each other, or whose type doubles at each step, under both rule sets and with a timeout, a signature of dictionaries of dictionaries nine levels deep, a type inferred through two thousand calls, a type written hundreds of levels deep in a declaration, a type shown in short, and a report that a bound made strict, whose message says so, for a deep and for a wide type |
| `StaticFieldTests` | A read or a write of a static field of a generic type as a member use, its `F:` line, and the framework's lines |
| `ProbeTests` | The probe projects under `Probes`, each with a `baseline.txt` of every warning by file, line, column and ID |

`ProbeTests` guards against a change that loosens the analyzer by mistake. The probes are programs written to find
what the analyzer misses or gets wrong, about 2,200 warnings in all. A warning that goes away shows there even where no test of its own
names the form. Changing a baseline is a deliberate edit that shows in a diff; set the environment variable `SCA_PROBE_WRITE` to a
folder to have a run write the warnings that it got there, in the form of a baseline.

**Deep code is tested on a stack of a fixed size.** Where SCA1048 comes depends on the stack that is left on the thread that runs the
analyzer, and on the size of its frames, which changes about tenfold between a method that the JIT has not optimized yet and one that
it has. The testing library runs the analysis on threads of the thread pool, so a test that sat between "deep enough for the analyzer to
give up" and "shallow enough for the compiler" lost its report in one run and ended the process in another. `DeepCodeTests` runs the
analyzer through `FixedStackAnalyzer`, a wrapper of the test project that runs each action of the analyzer on a thread of its own with
a stack of a given size: 192 KB where a member must be too deep, and 16 MB where the analyzer must get through. The analyzer itself has
no setting for it. The compiler still makes the operations of a member on a thread of the pool, by recursion for a conditional
expression and a chain of calls, so those tests stay at about half of the depth at which the compiler has run out of stack, and about
twice the most that the analyzer reaches on the small stack with every method optimized. A sum of binary operators, which the compiler
makes without recursion, carries the tests of what SCA1048 says, at a length far beyond both. The class's remarks give the measured
depths.

Run the tests with `dotnet test Tests/Singulink.CodeAnalysis.Tests -c Release`. The stress tools in `Tests/Stress` are not part of
that run. Run them before a release or after a change to the flow rules; their README says how.

#### Adding a Diagnostic

A new ID touches all of these:

1. `DiagnosticIds.cs`: the ID, in the group of its range (see the table of groups in
   [What the Analyzer Does](Docs/articles/start/what-it-does.md)).
2. `Rules.cs`: the rule or rules, and each one in `Rules.All`; and the ID in its group in `Rules.GetGroup`.
3. `Verify.cs`: a helper per rule, with the whole message; and the ID in `EnforcedIds` if it is in the enforced group.
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
2. Add the line to `NonCopyableSymbols.txt`. A member that exists only from .NET 11 on gets the trailing comment `# net11+`, and a
   symbol that only the .NET Framework has gets `# netfx`.
3. Run the tests on both targets: `FrameworkScanTests` checks the members that reach the built-in types against the runtime that
   the tests run on, and the run of net10.0 checks the .NET Framework 4.8.1 on a machine that has it.
4. Update [Built-in Framework Symbol List](Docs/articles/rules/framework-symbol-list.md) where the change is one that a user would
   notice.

The evidence file is not part of either package, and is not embedded in the analyzer: only `NonCopyableSymbols.txt` is.
