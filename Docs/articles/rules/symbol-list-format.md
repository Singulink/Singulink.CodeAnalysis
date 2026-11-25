<div class="article">

# Symbol List Format

A symbol list file tells the analyzer what it cannot see for itself in code that you cannot mark. This page gives the exact format, every marker, and how entries combine. [Symbol List](../guides/symbol-list.md) shows where the files go and how to use them, and why most projects never need one.

### At a Glance

- **One entry per line:** a [documentation comment ID](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/#id-strings), with an optional marker after a semicolon, or the simple name of an assembly after `A:` with the marker `trusted`.
- **Comments:** a line that starts with `#`, and the rest of a line after white space and `#`.
- **The ID to add** is in the message of SCA1020.
- **Entries are exact per overload.** There is no entry for every member of a type, and no wildcard.
- **The files:** the project's own symbol list files and those that come with a library are read alike and merged with the [built-in framework symbol list](framework-symbol-list.md).
- **Where two entries disagree,** the one that claims less applies, even between a project's file and a library's. Two exceptions: `copyable` overrides the framework symbol list, and `clones=complete` counts where any line for the class says it, in any file.
- **Some lines of a symbol list file get a warning (SCA1042):** a line that cannot be parsed; a line that has no effect, such as one that claims more of a member than the framework symbol list; an outcome of `borrowswhen` that the analyzer does not know; and a line that says a member which clones makes no copy. The analyzer's own tests check the framework symbol list, so it never gets one.

## Lines

```text
# Members: make no copy other than the value they hand out
M:ThirdParty.Bag`1.#ctor
M:ThirdParty.Bag`1.Add(`0)
P:ThirdParty.Bag`1.Item(System.Int32)

# new: what they hand out is new, whatever they were called on
M:ThirdParty.Bag`1.Take;new

# borrows: a stored value may be passed to it by value; it reads the copy during the call and keeps nothing of it
M:ThirdParty.Native.GetAddress(ThirdParty.Handle);borrows

# borrows also says that a member keeps nothing of a container that it is given through a parameter over its type parameter
M:ThirdParty.Sequences.IsSorted``1(System.Collections.Generic.List{``0});borrows

# drains: the sequence that it returns takes each element out of its source as it hands it out
M:ThirdParty.Feed`1.TakeAllAsync(System.Threading.CancellationToken);drains

# owned: the task of the property belongs to its object alone, and no other member hands it out
P:ThirdParty.Promise`1.Task;owned

# borrowswhen: the method keeps nothing of the value that it is given where it returns false
M:ThirdParty.Pool`1.TryReturn(`0);borrowswhen=false

# for=: the entry vouches only for these type parameters
M:ThirdParty.Map`2.Add(`0,`1);for=TValue
M:ThirdParty.Map`2.Remove(`0,`1@);new;for=TValue

# A static field of a generic type: the static initializers of the type make no copy
F:ThirdParty.Cache`1.Shared

# view: copying the struct does not copy the elements
T:ThirdParty.Slice`1;view

# noncopyable: a struct that cannot be marked in its source
T:ThirdParty.Handle;noncopyable

# unsafetocopy: as noncopyable, and a copy of the struct can break memory safety
T:ThirdParty.PinnedBlock;unsafetocopy

# copyable: a struct that the framework symbol list or a library makes non-copyable, and that this project copies on purpose
T:System.Runtime.InteropServices.GCHandle;copyable

# erasable: no base type or interface of the type hands out a value of its type arguments
T:ThirdParty.Promise`1;erasable

# erasable;for=: the same, for the type arguments of these type parameters only
T:ThirdParty.Index`2;erasable;for=TPayload

# trusted: an assembly that was not built with the analyzer, and whose code makes no copy of the non-copyable values that it gets at
A:Contoso.Plugins;trusted

# clones: a class whose own code clones its objects field by field with MemberwiseClone, and the method that does it
T:Contoso.Document;clones
M:Contoso.Document.Copy;clones

# clones=complete: the class's clones lines name every member that clones its object or hands it on
T:Contoso.Report;clones=complete
M:Contoso.Report.Copy;clones

# clones=none: neither the class nor a class above it clones its objects
T:Contoso.Page;clones=none
```

## Markers

| Entry | Marker | In short |
|---|---|---|
| A member (`M:`, `P:`, `E:`) | none | Safe to use; hands out a value as new as the object and the arguments of the call |
| A member | `new` | Safe to use; hands out a new value |
| A member | `borrows` | Uses what it is given during the call, and keeps nothing of it |
| A method that returns a sequence | `drains` | The sequence takes each element out of its source |
| An instance property of a class, of type <xref:System.Threading.Tasks.Task`1> | `owned` | The task belongs to the object alone |
| A method that returns `bool` | `borrowswhen=true` or `borrowswhen=false` | Safe to use; keeps nothing of a value where it returned that |
| A member | `for=T1,T2` | The entry vouches only for these type parameters |
| A generic class or interface (`T:`) | `erasable;for=T1,T2` | No base type or interface hands out a value of these type parameters |
| A static field of a generic type (`F:`) | none, or `for=T1,T2` alone | Reading or writing the field is safe: the static initializers of its type make no copy. See [Static Fields](#static-fields) |
| A struct (`T:`) | `view` | Copying the struct copies no elements |
| A struct (`T:`) | `noncopyable` | The struct is a non-copyable type |
| A struct (`T:`) | `unsafetocopy` | The struct is a non-copyable type, and a copy of it can break memory safety |
| A struct (`T:`) | `copyable` | The struct is an ordinary struct for the project |
| An assembly (`A:` and its simple name) | `trusted` | The unchecked assembly makes no copy of what it gets at |
| A generic class or interface (`T:`) | `erasable` | No base type or interface hands out a value of its type arguments |
| A class (`T:`) | `clones` | The class's own code clones its objects field by field with MemberwiseClone |
| A class (`T:`) | `clones=complete` | As `clones`, and its `clones` lines name every member that clones the object or hands it on |
| A class (`T:`) | `clones=none` | Neither the class's own code nor that of a class above it clones its objects |
| A method (`M:`), a property (`P:`) or an event (`E:`) | `clones` | The member clones its object, or the object that it is given, field by field with MemberwiseClone, or hands it to code that can |

#### No Marker

A member with no marker is safe to use. What it hands out is as new as the object and the arguments of the call. A property covers its getter and setter.

#### New

A member marked `new` is safe to use, and what it hands out is a new value, whatever it was called on.

#### Borrows

You may pass a stored value of a non-copyable type by value to a member marked `borrows`. You may also pass a container to one of its parameters whose type mentions a type parameter that is not marked: without the marker, such a parameter is [assumed to keep the container](keeping-containers.md).

The member uses what it is given during the call and keeps nothing of it. Everything else about the call stays as it is. It is the only marker that is useful on a member that is not generic.

#### Drains

A method that returns a sequence (<xref:System.Collections.Generic.IEnumerable`1>, <xref:System.Collections.Generic.IAsyncEnumerable`1>) and is marked `drains` is safe to use. The sequence takes each element out of its source as it hands it out, so nothing hands an element out twice.

- The sequence is a new container, whatever the method was called on: what a `foreach` or an `await foreach` over it hands out is new.
- A second enumeration takes further elements, never the same ones.
- As with `new`, this applies to values of a non-copyable type: a container that is taken out is a reference that whoever put it in may still hold.
- A method that hands out its elements and keeps them, as an ordinary enumeration does, must not have the marker.

#### Owned

An instance property of a class, of type <xref:System.Threading.Tasks.Task`1>, marked `owned`, is safe to use, and the task belongs to the object alone:

- it is made with the object;
- no constructor takes it from somewhere else;
- it is never replaced;
- no other member hands it or its result out.

Its one read on an object that the method has just made is a new value, as for <xref:System.Threading.Tasks.TaskCompletionSource`1.Task?displayProperty=nameWithType>, the one built-in entry. See [Task of a Completion Source](listed-members.md#task-of-a-completion-source).

#### Borrowswhen

A method marked `borrowswhen` is safe to use, as with no marker. The marker makes the claim of [`borrows`](#borrows), that the method uses the value during the call and keeps nothing of it, for one result of the call only. Its value is that result: `true` or `false`. Where the method returned the other result, or threw, it has taken the value.

| Line | Keeps nothing of the value where it | Has taken the value where it |
|---|---|---|
| `borrowswhen=false` | Returned false | Returned true, or threw |
| `borrowswhen=true` | Returned true | Returned false, or threw |

```text
M:ThirdParty.Pool`1.TryReturn(`0);borrowswhen=false
```

```cs
if (!pool.TryReturn(lease))
    lease.Dispose();                     // OK: TryReturn has not taken the lease here

bool returned = pool.TryReturn(other);
if (!returned)
    other.Dispose();                     // SCA1016: the result went into a local first
```

Where the result counts:

- **Only where the result goes straight into a condition.** That is an `if`, a loop, `!`, `&&`, `||` or `?:`, a comparison with the constant `true` or `false`, and `is true`, `is false`, `is not true` and `is not false`. A result that is stored, passed on, or combined with `&` or `|` counts as one where the method took the value. This is the limit that `[NotNullWhen]` has.
- **On the branch where the method has not taken the value,** a use of the variable is no use after a hand-off (SCA1016), and does not keep the call from being the last use.
- **A result of `Task<bool>` or `ValueTask<bool>`** counts where the call is awaited where it is made, directly or behind `ConfigureAwait`. A task that is stored first counts as one where the method took the value.
- **Never where the method throws.** A method that throws may have taken the value, so a `catch` that uses the variable after the call gets a warning, as after any hand-off (see [Exception Paths](last-use.md#exception-paths)).

What the line covers:

- **A method that is called by its name** and returns `bool`, `Task<bool>` or `ValueTask<bool>`. A constructor, an operator, a conversion and an accessor cannot have the marker.
- **Values only.** The marker covers each parameter that the method takes by value whose type in the method's definition is a struct or a type parameter that can be a struct. With `for=` after the marker, it covers only the type parameters that `for=` names. Unlike `borrows`, it says nothing about a container that the method is given, and it has no effect on the [`[Kept]` check](keeping-containers.md).
- **Not a stored value.** A field or an element that is passed by value to such a method still gets a warning, since where the method takes it there are two. The marker helps a local or a by-value parameter, at its [last use](last-use.md).

How to write it:

- **Outcomes go after the `=`,** separated by commas, each once, with white space allowed around the `=` and the `,`. `true` and `false` together cannot be parsed: for a method that keeps nothing of the value whatever it returns, write `borrows`.
- **An outcome that the analyzer does not know is left out,** so the line claims less, and a line whose every outcome is unknown counts as a line with no marker. The project that owns the file gets a warning for it (SCA1042), and a project that gets the file from a library does not.
- **Read the method first.** The analyzer does not check the line, also for a method that the project declares itself. Write the marker only where the method returns that result before it keeps anything of the value.

#### For

`for=T1,T2` goes after the marker, if any. The entry vouches only for these type parameters of the member or of its containing types. When a non-copyable type is bound to any other type parameter, the member counts as not on the symbol list.

On a type, `for=` follows `erasable` only, and names type parameters of the type or of its containing types: see [Erasable](#erasable).

#### View

A struct marked `view` copies no elements when it is copied, as <xref:System.Span`1> does. It is a container and not a non-copyable type.

#### Noncopyable

A struct marked `noncopyable` is a non-copyable type.

A `noncopyable` line for a struct that its `[NonCopyable]` attribute makes non-copyable already has no effect and gets a warning (SCA1042). So does one for a struct that its attribute makes unsafe to copy, which is non-copyable as well. A line for a type of the [built-in framework symbol list](framework-symbol-list.md) gets no warning: one file can serve a project that [leaves the built-in non-copyable types out](../guides/built-in-types.md), where the line makes the type non-copyable. The line also has an effect where a `copyable` line takes the struct out.

#### Unsafetocopy

A struct marked `unsafetocopy` is a non-copyable type, as with `noncopyable`, and a copy of it can break memory safety, as `[NonCopyable(UnsafeToCopy = true)]` says: see [Unsafe to Copy](../guides/marking-a-struct.md#unsafe-to-copy). An entry has one of the two markers. Under the current memory safety rules they do the same, and under the [updated memory safety rules](updated-memory-safety-rules.md) a copy of a struct marked `unsafetocopy` is an error.

| Line | Effect |
|---|---|
| `unsafetocopy` for a library's struct that is only `[NonCopyable]` | The struct is unsafe to copy in this project |
| `unsafetocopy` for a struct that has no attribute | The struct is non-copyable, and unsafe to copy |
| `unsafetocopy` for a struct that its attribute marks unsafe to copy | No change, and a warning (SCA1042) |
| `noncopyable` for a struct that its attribute marks unsafe to copy | No change, and a warning (SCA1042): the struct stays unsafe to copy |
| `noncopyable` or `unsafetocopy` for a struct that the framework symbol list marks unsafe to copy | No change where the project includes the built-in non-copyable types, and no warning, since a project that leaves them out needs the line |
| `copyable` for a struct that is unsafe to copy | The struct is an ordinary struct, as `copyable` makes any struct |

Nothing makes a struct that is unsafe to copy plain non-copyable. Only `copyable` takes it out, and then it is no non-copyable type at all.

#### Copyable

A struct marked `copyable` is an ordinary struct for this project, even though the [built-in framework symbol list](framework-symbol-list.md) or a referenced library makes it non-copyable. Use it for one of the [built-in non-copyable types](../guides/built-in-types.md) that the project copies on purpose, or for a library's struct that another analyzer's attribute marks.

> [!NOTE]
> **A `copyable` line counts only in a file of your project.** It says that your project copies the struct on purpose, which only your project can decide. The same line in a file that comes with a package does nothing, and gets no warning, in the projects that use the package. In the library's own build it is a file of the project, and counts there. A file comes with a package wherever NuGet put the package: in the global packages folder, in a fallback folder, or in the packages folder of a project that uses `packages.config`.

Everything that depends on the struct changes with it. A struct with a field of it, a tuple or a `Nullable` of it, a `List<T>` of it, and a marked type parameter bound to it are all ordinary types too. Nothing that the analyzer guarantees holds for them.

A `copyable` line gets a warning (SCA1042), and does nothing, where it names:

| Names | Instead |
|---|---|
| Something that is not a struct, or nothing at all | Fix the ID |
| A struct that the project declares | Take its `[NonCopyable]` off |
| A struct that is non-copyable only through what it holds, such as <xref:System.Threading.Tasks.ValueTask`1> | Nothing to do: the struct itself is copyable |

#### Trusted

An assembly marked `trusted` was not built with the analyzer, and its code makes no copy of the non-copyable values that it gets at. That holds even where its code runs behind a type of checked code or of the framework. The name is matched without regard to case, as the runtime binds it.

The line turns off two warnings for the assembly: SCA1025, and SCA1009 on a field of a class of the project that derives from one of its classes (see [SCA1009](../diagnostics/sca1009.md#a-base-class-that-is-not-known)). It changes nothing else: its members stay unchecked code for every other rule. See [SCA1025](../diagnostics/sca1025.md).

#### Clones

A class marked `clones` has code of its own that clones its objects field by field with <xref:System.Object.MemberwiseClone>. A class of the project that derives from it, at any depth, gets a warning on each field that holds a non-copyable value (SCA1009), since the clone copies the field. One line covers the class, every class that derives from it, and the generic classes that derive from it, where a non-copyable type is bound to them: see [Base Classes That Clone Themselves](declarations.md#base-classes-that-clone-themselves).

```text
# Document of a library clones itself in its Copy method
T:Contoso.Document;clones
```

```cs
sealed class PinnedDocument : Contoso.Document
{
    private GCHandle _pinned;                            // SCA1009: Document's own code clones the object, and the handle with it
    private readonly List<GCHandle> _handles = new();    // OK: the clone shares the list
}
```

| Question | Answer |
|---|---|
| Where can the line stand? | In any symbol list file: the project's own, and one that comes with a library. It counts whether or not the class's assembly was built with the analyzer, since it only adds warnings |
| Can another line take it away? | No. A line for the same class in another file does not turn it off, and a `clones=none` line for the class gets a warning and is left out (SCA1042). Suppress the warning where a class is never cloned |
| What if one file says `clones=complete` and another `clones` alone? | `clones=complete` applies. A plain `clones` line says nothing about completeness. Where the lines miss a member, add a `clones` line for that member, in any file |
| May the class be the project's own? | Yes, where its own code clones its objects by a means that the analyzer does not see. A call of `MemberwiseClone` in the project still gets SCA1005 and SCA1008 |
| Which classes of the framework have it? | Every one that clones itself: see [Classes That Clone Themselves](framework-symbol-list.md#classes-that-clone-themselves) |

A method, a property or an event marked `clones` clones its object, or the object that it is given, or hands it to code that can. For an event, adding a handler and removing one both count, and the analyzer warns at each. Two examples are `M:System.Globalization.CultureInfo.Clone` and `P:System.Drawing.Printing.PrinterSettings.DefaultPageSettings`, whose page settings refer back to the printer settings. The built-in framework symbol list has a line for each public or protected member of the framework that clones or hands its object on, through other members included.

A use of a member marked `clones` gets a warning with no advice to add a line, because no line can say that such a member makes no copy. A line for such a member that says it makes no copy, with no marker or with `new`, `borrows`, `drains` or `owned`, gets a warning and is left out.

**Each line of a class that clones itself vouches for what it names,** and the type line says whether the lines are complete:

| You write | You vouch for |
|---|---|
| The type line alone, `T:Contoso.Document;clones` | The class clones itself. The analyzer counts every member that no line names as one that can clone |
| A `clones` member line, `M:Contoso.Document.Copy;clones` | This member clones its object, or the object that it is given, or hands it to code that can |
| A member line with no marker, `M:Contoso.Document.WithTitle(System.String)` | This member neither clones its object nor hands it on |
| The type line with `complete`, `T:Contoso.Document;clones=complete` | The `clones` lines name every member that clones the object or hands it on, so a member with no line does neither |

> [!WARNING]
> `clones=complete` vouches that the `clones` lines name **every** member that clones the object or hands it to other code, even one that only gives it to a visitor interface, a registry or a wrapper. The analyzer cannot check that. Without `complete`, the analyzer judges each member by its own line: the safe choice when you cannot vouch for all of them.

Take a use of a member that no line names, on an object whose class binds a non-copyable type. It gets a warning unless the class's line says `clones=complete`. With `clones=complete`, it gets a warning only where the member's signature shows that it can hand the object on: see [Uses](declarations.md#uses).

A member line names a member that another assembly can call, and that clones the object or hands it on directly or through another member. A private or internal member is not imported where the library is referenced, so a line for it has no effect there and gets SCA1042: name the member that calls it instead. A `clones` line for an override also puts the warning on the members of its base class, which can run it: see [Overrides That a Base Class Runs](declarations.md#overrides-that-a-base-class-runs).

A class can sit between the class that clones itself and the object's own class. Where it is of a library that was not built with the analyzer, it takes a type line of its own for its own members, such as ``T:Contoso.TaggedDocument`1;clones=complete``. The class above it already has a `clones` line, but this line is not redundant, so it gets no SCA1042 warning for having no effect.

A library that is built with the analyzer gets SCA1008 for each member of its own such class that another assembly can call and that clones its object without a line. The warnings help it complete the file that it ships, before it writes `clones=complete` on the type line. It is not a proof: a member that clones through a private helper, or hands the object on, needs its line all the same. For a library that is not built with the analyzer, the lines are your statement, as every line is.

Two other lines are for such a class:

- **A member line with no marker** says that the member never hands the object itself to other code. It answers two warnings: the one for a member whose signature shows that it can hand the object on, and the one for any member of a class whose line does not say `clones=complete`. It vouches for everything that the member does, what it calls included. It does not vouch for an override of it, or for an [override that it runs](declarations.md#overrides-that-a-base-class-runs) in a class below the class that clones itself.
- **`borrows`,** for a member that receives the object as an argument, says that it uses the object during the call and keeps nothing of it. A member on the list with no marker that receives a value as a type that hides it, as `List<object>.Add` does, keeps it, and the conversion gets its warning.

A member line with no marker gets no warning for having no effect: it can still be what lets a container of an unmarked generic through (SCA1023).

**A class marked `clones=none` never clones its objects,** and neither does a class above it. The line is for a base class of a library that was not built with the analyzer, where nothing else tells the analyzer what the class does. Without it, a class of the project that derives from such a class gets a warning on each field that holds a non-copyable value: see [SCA1009](../diagnostics/sca1009.md#a-base-class-that-is-not-known).

The analyzer cannot check the line, so write it only for a class whose code you know. Where a `clones` line names the class or a class above it, that line applies, and the `clones=none` line gets a warning.

#### Erasable

A generic class or interface marked `erasable` has no base type or interface that hands out a value of its type arguments. So a container of the type may be converted to one of them. See [Variance and Erasable Types](variance-and-erasable-types.md).

With `for=`, the line says so for the type parameters that it names, of the type or of a type that contains it. A conversion to a base type or an interface then hides nothing of what is bound to them, and is judged by the other type arguments alone:

```text
# The keys of an index hand out keys and never a payload
T:ThirdParty.Index`2.KeyList;erasable;for=TPayload
```

Write the line only where the code behind those base types and interfaces never copies such a value either: a conversion hands the object to that code. The framework symbol list marks the key collection of a `Dictionary<TKey, TValue>` this way: see [Dictionary Keys](framework-symbol-list.md#dictionary-keys).

## Rules for Entries

#### What May Be on the Symbol List

- **A member may be on the symbol list** if it makes no copy of a non-copyable value other than the one it hands out. A member that moves values inside its container, like a `List<T>` that grows, qualifies. One that leaves two live instances, like `ToArray` or `CopyTo`, does not.
- **A static field of a generic type may be on the symbol list** if the static constructor and the static field initializers of its type make no copy of a value of its type arguments.
- **The symbol list is for code you cannot change.** Your own generics mark their type parameters instead.

#### Static Fields

Reading or writing a static field of a generic type runs the static constructor and the static field initializers of the type, with the type arguments of the use. That code can copy a value of a type argument, so the analyzer counts such a use as it counts a call of a member of the type. With a non-copyable type bound to a type parameter that is not marked, the use gets a warning (SCA1020) unless a line names the field.

```text
F:ThirdParty.Cache`1.Shared
```

```cs
ThirdParty.Cache<LockGuard>.Shared = myLock.Enter();     // OK with the line; SCA1020 without it
string name = ThirdParty.Cache<LockGuard>.Name;          // OK: a constant runs no code
```

The line says that the static constructor and the static field initializers of the type make no copy of a value of its type arguments. Read them first: the analyzer takes the line on trust.

| Question | Answer |
|---|---|
| Which fields take a line? | A static field, other than a constant, of a generic type or of a type nested in one |
| Which markers fit? | None. `for=` can follow the ID, as on a member. A read of a field gets the value that the field holds, so `new` cannot fit, and a field takes no argument for `borrows` to speak of |
| What about a static property, method or event? | It is a member like any other, and its own line covers the static initializers of its type as well |
| Does a line for any other field do something? | No. A line for an instance field, a constant, or a static field of a type that is not generic gets a warning (SCA1042), since reading or writing such a field runs no code with the type arguments of the use |

#### Members of Extension Blocks

A member of an extension block has its entry under the ID of its implementation method. That is the static method that the compiler makes for it in the class that the block is in. It has the receiver as its first parameter, and the type parameters of the block before its own. For `extension<T>(Bag<T> bag) { public void TouchWith<U>(ref U other) { } }` in `BagExtensions` that is ``M:BagExtensions.TouchWith``2(Bag{``0},``1@)``, and `for=` names `T` or `U` as they are written.

- A property has its entry under the method of its getter (`get_Size`), or of its setter if it has no getter.
- The ID that the compiler gives the member inside the block is accepted as well, but it carries a generated name that changes with the declaration of the receiver.
- A warning that asks for an entry names the implementation method.

#### Lines That Are Ignored or Get a Warning

An entry that names something the project does not reference is ignored, so one file can serve several projects. These get a warning (SCA1042):

| Line | Warning when |
|---|---|
| `noncopyable`, `unsafetocopy` | It names no type, or a struct that is what the line says already |
| A line that cannot be parsed | Always; it is then ignored |
| `copyable` | It has no effect |
| `trusted` | It has no effect |
| `clones=none` | It has no effect |
| `clones` | It names no type, method, property or event, or has no effect |
| A member line of any marker that names an accessor, such as `M:Contoso.Document.get_Title` or `M:Contoso.Document.add_Changed(System.EventHandler)` | Always: the analyzer looks up every use of an accessor under its property or event, so write the `P:` or `E:` line instead |
| A member line with no marker, `new`, `borrows`, `drains` or `owned` | A `clones` line names the member; the line is left out |
| A member line for a member that the framework symbol list names | It names the member for a type parameter that the framework symbol list leaves out, lists an outcome of `borrowswhen` that the framework symbol list does not, or says `borrows` where the framework symbol list says `borrowswhen` or no marker: see [Entries That Disagree](#entries-that-disagree) |
| `borrowswhen` | An outcome is not `true` or `false`; the line is taken without it |
| A field line (`F:`) | It names an instance field, a constant, or a static field of a type that is neither generic nor nested in a generic type |

- **A `noncopyable` or `unsafetocopy` line that names no type** gets a warning because a mistyped ID would leave the type copyable in a build that looks clean. A project that does not reference the type should take the line out of its file.
- **A `noncopyable` or `unsafetocopy` line for a struct that is what it says already** by its attribute has no effect. A line of a file that came in a package gets this warning only in the library's own build. A line for a type of the framework symbol list gets none, since a project that leaves the built-in non-copyable types out needs it.
- **A `copyable` line has no effect** in the cases that [Copyable](#copyable) gives.
- **A `trusted` line has no effect** for an assembly that the project does not reference, for the project's own, and for one that was built with the analyzer. Nor does it where the line answers nothing: the assembly gets no SCA1025, and no class of the project that derives from one of its classes holds a non-copyable value in a field.
- **A `clones=none` line has no effect** for a class of the project, of the framework or of a library that was built with the analyzer, since the analyzer already knows what those do. Nor does it for a class that is static or sealed, since no class can derive from it, or where a `clones` line names the class or a class above it.
- **A `clones` line has no effect** for a class that is static, or sealed and not generic, since no class can derive from it and no type can be bound to it. A line that names no type, no method, no property or no event gets a warning as a `noncopyable` line does, since a mistyped ID would leave the fields of every derived class with no warning. A line that repeats one of the built-in framework symbol list, or that names a class whose base class has a line, gets no warning: it has its effect already, and with `clones=complete` it covers the members of the class.

A line cannot be parsed if it has:

- an ID that cannot be a documentation comment ID, such as one with a parenthesis that is not closed;
- a `for=` that names a type parameter which the member or the type does not have, or one that fits more than one type parameter of it and of the types that contain it;
- a `for=` on a type with a marker other than `erasable`;
- a marker on something that it does not fit (see the table below), or any marker on a field line;
- `clones=complete` or `clones=none` on a method, a property or an event, a value of `clones` other than `complete` or `none`, or `complete` or `none` written as a marker of its own;
- a `borrowswhen` with no outcome, an empty outcome, an outcome listed twice, or both `true` and `false`;
- a marker that the analyzer does not know: the whole line is ignored;
- a marker other than `trusted` on an assembly, or `trusted` on anything but an assembly;
- an assembly given by a display name with a version, or by a file name.

| Marker | Fits only |
|---|---|
| `view`, `noncopyable`, `unsafetocopy`, `copyable` | A struct |
| `erasable` | A generic class or interface |
| `clones` | A class, a method, a property or an event |
| `clones=complete`, `clones=none` | A class |
| `new`, `borrows` | A member, not a type |
| `drains` | A method whose result is an <xref:System.Collections.Generic.IEnumerable`1> or an <xref:System.Collections.Generic.IAsyncEnumerable`1> |
| `owned` | An instance property of a class whose type is <xref:System.Threading.Tasks.Task`1> |
| `borrowswhen` | A method that is called by its name, not a constructor, an operator, a conversion or an accessor, that returns `bool`, `Task<bool>` or `ValueTask<bool>`, and that has a parameter which the marker covers |

#### Entries That Disagree

Where two entries for the same ID disagree, the one that claims less applies, whether they are in two symbol list files or in a file and the [built-in framework symbol list](framework-symbol-list.md). A file that comes with a library counts like the project's own, except for a `copyable` line, which counts only in the project's own file:

| Entries | What applies |
|---|---|
| `noncopyable` and `view` | `noncopyable` |
| `unsafetocopy` and `noncopyable` or `view`, in two files or in a file and the framework symbol list | `unsafetocopy`: a line that says a copy can break memory safety only makes the analyzer stricter |
| No marker and `new`, `borrows`, `drains`, `owned` or `borrowswhen` | No marker |
| `borrows` and `borrowswhen` | The `borrowswhen` line |
| Two `borrowswhen` lines | The outcome that both list: `false` and `false` give `false`, and `false` and `true` give no marker |
| Two `for=` limits | Only the type parameters that both name |
| Two `erasable` lines of a type with `for=` limits | Erasable for the type parameters that both name, and for none where they name none in common |
| `copyable` in a file of the project, and the framework symbol list or an attribute | `copyable`, since that is what it is for |
| `copyable` in a file that comes with a package | Nothing: the line does nothing in a project that uses the package |
| `copyable` and `noncopyable` or `unsafetocopy` of the same type, in two symbol list files | `noncopyable` or `unsafetocopy` |
| `clones` and any other line for the same class | Both: `clones` is kept apart, and nothing takes it away |
| `clones` and a line that says the same method makes no copy | `clones`: the other line is left out, with a warning |
| `clones=complete` and `clones` alone, for the same class | `clones=complete`: a plain `clones` line says nothing about completeness, and nothing takes `complete` away |
| `clones=none`, and `clones` for the same class or a class above it | `clones`: the `clones=none` line has no effect, and gets a warning |

So a file can add entries and can tighten a built-in one, but cannot loosen it. Two kinds of line do loosen: `copyable`, and `clones=complete`, by which you vouch for the members of a class.

A `borrowswhen` line of a file cannot add an outcome to a member that the framework symbol list names, nor add the marker to one that it names without it. Such a line gets a warning that names the outcome that the framework symbol list does not list (SCA1042), and the entry of the framework symbol list applies. Which members of the framework have the marker: [Members That Take a Value Only Where They Return True](framework-symbol-list.md#members-that-take-a-value-only-where-they-return-true).

A `borrows` line claims every outcome, so it claims more than a `borrowswhen` line or a line with no marker of the framework symbol list. It gets the same warning, which says what the framework symbol list has for the member, and the entry of the framework symbol list applies:

```text
# SCA1042: the framework symbol list names TryWrite with 'borrowswhen=false'
M:System.Threading.Channels.ChannelWriter`1.TryWrite(`0);borrows
```

A `borrows` line whose `for=` leaves out a type parameter that the framework symbol list covers gets no such warning, since it takes the entry away for that type parameter.

Take a member that the framework symbol list names with `for=`. A line of a file that names it for a type parameter that the `for=` leaves out, or names it with no `for=`, would add nothing. The analyzer leaves such a line out and warns about it (SCA1042). Where code uses the member, the warning says which type parameters the list names it for, and gives no line to add. An `erasable` line of a type that the framework symbol list marks `erasable` for some type parameters only is treated the same way.

#### Owned Entries Are Taken on Trust

An `owned` entry is a promise about the whole type, and the analyzer takes it on trust. The task is made with the object, no constructor takes it from somewhere else, and nothing but the property hands it out. The analyzer checks one part of it: that the object is created with no argument that carries a non-copyable type. That check finds nothing to look at where the type is not generic.

So an `owned` entry is wrong for a type whose constructor can take over the task of another object of it. Every object made that way hands out the same task, and nothing warns about it.

A struct cannot keep the promise at all: a copy of the struct shares the task, and a call on either one can replace the task. So the marker is not accepted on a struct.

#### An Entry Speaks for the Type

An entry for a member of a type also says that the type never hands itself to another holder, as a method that stores `this` in a field or a collection would.

#### Members of Unchecked Code

On a member of an assembly that was not built with the analyzer, two markers say what the analyzer cannot see:

- `borrows` is needed on a member that gets at a non-copyable value other than by value: by a reference, in a container, in a view or through a pointer. Without it the use gets a warning (SCA1024).
- `new` is needed on a member for a container that it hands out to count as new: without it the container is stored.

See [Code Not Built With the Analyzer](unchecked-code.md).

</div>
