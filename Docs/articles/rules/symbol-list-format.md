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
- **Three kinds of line of a symbol list file get a warning (SCA1042):** a line that cannot be parsed; a `noncopyable`, `copyable`, `trusted` or `clones` line that has no effect; and a line that says a member which clones makes no copy. The analyzer's own tests check the framework symbol list, so it never gets one.

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

# for=: the entry vouches only for these type parameters
M:ThirdParty.Map`2.Add(`0,`1);for=TValue
M:ThirdParty.Map`2.Remove(`0,`1@);new;for=TValue

# view: copying the struct does not copy the elements
T:ThirdParty.Slice`1;view

# noncopyable: a struct that cannot be marked in its source
T:ThirdParty.Handle;noncopyable

# copyable: a struct that the framework symbol list or a library makes non-copyable, and that this project copies on purpose
T:System.Runtime.InteropServices.GCHandle;copyable

# erasable: no base type or interface of the type hands out a value of its type arguments
T:ThirdParty.Promise`1;erasable

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
| A member | `for=T1,T2` | The entry vouches only for these type parameters |
| A struct (`T:`) | `view` | Copying the struct copies no elements |
| A struct (`T:`) | `noncopyable` | The struct is a non-copyable type |
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

#### For

`for=T1,T2` goes after the marker, if any. The entry vouches only for these type parameters of the member or of its containing types. When a non-copyable type is bound to any other type parameter, the member counts as not on the symbol list.

#### View

A struct marked `view` copies no elements when it is copied, as <xref:System.Span`1> does. It is a container and not a non-copyable type.

#### Noncopyable

A struct marked `noncopyable` is a non-copyable type.

#### Copyable

A struct marked `copyable` is an ordinary struct for this project, even though the [built-in framework symbol list](framework-symbol-list.md) or a referenced library makes it non-copyable. Use it for one of the ten framework structs that the project copies on purpose, or for a library's struct that another analyzer's attribute marks.

Everything that depends on the struct changes with it. A struct with a field of it, a tuple or a `Nullable` of it, a `List<T>` of it, and a marked type parameter bound to it are all ordinary types too. Nothing that the analyzer guarantees holds for them.

A `copyable` line gets a warning (SCA1042), and does nothing, where it names:

| Names | Instead |
|---|---|
| Something that is not a struct, or nothing at all | Fix the ID |
| A struct that the project declares | Take its `[NonCopyable]` off |
| A struct that is non-copyable only through what it holds, such as <xref:System.Threading.Tasks.ValueTask`1> | Nothing to do: the struct itself is copyable |

#### Trusted

An assembly marked `trusted` was not built with the analyzer, and its code makes no copy of the non-copyable values that it gets at. That holds even where its code runs behind a type of checked code or of the framework. The line turns off SCA1025 for the assembly, and the warning on a field of a class of the project that derives from one of its classes (see [SCA1009](../diagnostics/sca1009.md#a-base-class-that-is-not-known)). It changes nothing else: its members stay unchecked code for every other rule. The name is matched without regard to case, as the runtime binds it. See [SCA1025](../diagnostics/sca1025.md).

#### Clones

A class marked `clones` has code of its own that clones its objects field by field with <xref:System.Object.MemberwiseClone>. A class of the project that derives from it, at any depth, gets a warning on each field that holds a non-copyable value (SCA1009), since the clone copies the field. One line covers the class, every class that derives from it, and the generic classes that derive from it, where a non-copyable type is bound to them: see [SCA1009](../diagnostics/sca1009.md).

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
| Where can the line stand? | In any symbol list file: the project's own, and one that comes with a library. It counts whether or not the class's assembly was built with the analyzers, since it only adds warnings |
| Can another line take it away? | No. A line for the same class in another file does not turn it off, and a `clones=none` line for the class gets a warning and is left out (SCA1042). Suppress the warning where a class is never cloned |
| What if one file says `clones=complete` and another `clones` alone? | `clones=complete` applies. A plain `clones` line says nothing about completeness. Where the lines miss a member, add a `clones` line for that member, in any file |
| May the class be the project's own? | Yes, where its own code clones its objects by a means that the analyzer does not see. A call of `MemberwiseClone` in the project gets SCA1005 and SCA1008 as before |
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

Take a use of a member that no line names, on an object whose class binds a non-copyable type. It gets a warning unless the class's line says `clones=complete`. With `clones=complete`, it gets a warning only where the member's signature shows that it can hand the object on: see [Uses](../diagnostics/sca1009.md#uses).

A member line names a member that another assembly can call, and that clones the object or hands it on directly or through another member. A private or internal member is not imported where the library is referenced, so a line for it has no effect there and gets SCA1042: name the member that calls it instead.

A class can sit between the class that clones itself and the object's own class. Where it is of a library that was not built with the analyzers, it takes a type line of its own for its own members, such as ``T:Contoso.TaggedDocument`1;clones=complete``. The class above it already has a `clones` line, but this line is not redundant, so it gets no SCA1042 warning for having no effect.

A library that is built with the analyzers gets SCA1008 for each member of its own such class that another assembly can call and that clones its object without a line. The warnings help it complete the file that it ships, before it writes `clones=complete` on the type line. It is not a proof: a member that clones through a private helper, or hands the object on, needs its line all the same. For a library that is not built with the analyzers, the lines are your statement, as every line is.

Two other lines are for such a class:

- **A member line with no marker** says that the member never hands the object itself to other code. It answers two warnings: the one for a member whose signature shows that it can hand the object on, and the one for any member of a class whose line does not say `clones=complete`. It vouches for everything that the member does, what it calls included, and not for an override of it.
- **`borrows`,** for a member that receives the object as an argument, says that it uses the object during the call and keeps nothing of it. A member on the list with no marker that receives a value as a type that hides it, as `List<object>.Add` does, keeps it, and the conversion gets its warning.

A member line with no marker gets no warning for having no effect: it can still be what lets a container of an unmarked generic through (SCA1023).

**A class marked `clones=none` never clones its objects,** and neither does a class above it. The line is for a base class of a library that was not built with the analyzers, where nothing else tells the analyzer what the class does. Without it, a class of the project that derives from such a class gets a warning on each field that holds a non-copyable value: see [SCA1009](../diagnostics/sca1009.md#a-base-class-that-is-not-known).

The analyzer cannot check the line, so write it only for a class whose code you know. Where a `clones` line names the class or a class above it, that line applies, and the `clones=none` line gets a warning.

#### Erasable

A generic class or interface marked `erasable` has no base type or interface that hands out a value of its type arguments. So a container of the type may be converted to one of them. See [Variance and Erasable Types](variance-and-erasable-types.md).

## Rules for Entries

#### What May Be on the Symbol List

- **A member may be on the symbol list** if it makes no copy of a non-copyable value other than the one it hands out. A member that moves values inside its container, like a `List<T>` that grows, qualifies. One that leaves two live instances, like `ToArray` or `CopyTo`, does not.
- **The symbol list is for code you cannot change.** Your own generics mark their type parameters instead.

#### Members of Extension Blocks

A member of an extension block has its entry under the ID of its implementation method. That is the static method that the compiler makes for it in the class that the block is in. It has the receiver as its first parameter, and the type parameters of the block before its own. For `extension<T>(Bag<T> bag) { public void TouchWith<U>(ref U other) { } }` in `BagExtensions` that is ``M:BagExtensions.TouchWith``2(Bag{``0},``1@)``, and `for=` names `T` or `U` as they are written.

- A property has its entry under the method of its getter (`get_Size`), or of its setter if it has no getter.
- The ID that the compiler gives the member inside the block is accepted as well, but it carries a generated name that changes with the declaration of the receiver.
- A warning that asks for an entry names the implementation method.

#### Lines That Are Ignored or Get a Warning

An entry that names something the project does not reference is ignored, so one file can serve several projects. These get a warning (SCA1042):

| Line | Warning when |
|---|---|
| `noncopyable` | It names no type |
| A line that cannot be parsed | Always; it is then ignored |
| `copyable` | It has no effect |
| `trusted` | It has no effect |
| `clones` | It names no type, method, property or event, or has no effect |
| A member line of any marker that names an accessor, such as `M:Contoso.Document.get_Title` or `M:Contoso.Document.add_Changed(System.EventHandler)` | Always: the analyzer looks up every use of an accessor under its property or event, so write the `P:` or `E:` line instead |
| A member line with no marker, `new`, `borrows`, `drains` or `owned` | A `clones` line names the member; the line is left out |

- **A `noncopyable` line that names no type** gets a warning because a mistyped ID would leave the type copyable in a build that looks clean. A project that does not reference the type should take the line out of its file.
- **A `copyable` line has no effect** in the cases that [Copyable](#copyable) gives.
- **A `trusted` line has no effect** for an assembly that the project does not reference, for the project's own, and for one that was built with the analyzer. Nor does it for an assembly that does neither of two things: reference an assembly which declares a non-copyable type, or declare a class that a class of the project derives from with a non-copyable value in a field.
- **A `clones=none` line has no effect** for a class of the project, of the framework or of a library that was built with the analyzers, since the analyzer already knows what those do. Nor does it for a class that is static or sealed, since no class can derive from it, or where a `clones` line names the class or a class above it.
- **A `clones` line has no effect** for a class that is static, or sealed and not generic, since no class can derive from it and no type can be bound to it. A line that names no type, no method, no property or no event gets a warning as a `noncopyable` line does, since a mistyped ID would leave the fields of every derived class with no warning. A line that repeats one of the built-in framework symbol list, or that names a class whose base class has a line, gets no warning: it has its effect already, and with `clones=complete` it covers the members of the class.

A line cannot be parsed if it has:

- an ID that cannot be a documentation comment ID, such as one with a parenthesis that is not closed;
- a `for=` that names a type parameter which the member does not have, or one that fits more than one type parameter of the member and of the types that contain it;
- a marker on something that it does not fit (see the table below);
- `clones=complete` or `clones=none` on a method, a property or an event, a value of `clones` other than `complete` or `none`, or `complete` or `none` written as a marker of its own;
- a marker other than `trusted` on an assembly, or `trusted` on anything but an assembly;
- an assembly given by a display name with a version, or by a file name.

| Marker | Fits only |
|---|---|
| `view`, `noncopyable`, `copyable` | A struct |
| `erasable` | A generic class or interface |
| `clones` | A class, a method, a property or an event |
| `clones=complete`, `clones=none` | A class |
| `new`, `borrows` | A member, not a type |
| `drains` | A method whose result is an <xref:System.Collections.Generic.IEnumerable`1> or an <xref:System.Collections.Generic.IAsyncEnumerable`1> |
| `owned` | An instance property of a class whose type is <xref:System.Threading.Tasks.Task`1> |

#### Entries That Disagree

Where two entries for the same ID disagree, the one that claims less applies, whether they are in two symbol list files or in a file and the [built-in framework symbol list](framework-symbol-list.md). A file that comes with a library counts like the project's own:

| Entries | What applies |
|---|---|
| `noncopyable` and `view` | `noncopyable` |
| No marker and `new`, `borrows`, `drains` or `owned` | No marker |
| Two `for=` limits | Only the type parameters that both name |
| `copyable` and the framework symbol list | `copyable`, since that is what it is for |
| `copyable` and `noncopyable` of the same type, in two symbol list files | `noncopyable` |
| `clones` and any other line for the same class | Both: `clones` is kept apart, and nothing takes it away |
| `clones` and a line that says the same method makes no copy | `clones`: the other line is left out, with a warning |
| `clones=complete` and `clones` alone, for the same class | `clones=complete`: a plain `clones` line says nothing about completeness, and nothing takes `complete` away |

So a file can add entries and can tighten a built-in one, but cannot loosen it. Two kinds of line do loosen: `copyable`, and `clones=complete`, by which you vouch for the members of a class.

Take a member that the framework symbol list names with `for=`. A line of a file that names it for a type parameter that the `for=` leaves out, or names it with no `for=`, would add nothing. The analyzer leaves such a line out and warns about it (SCA1042). Where code uses the member, the warning says which type parameters the list names it for, and gives no line to add.

#### The Owned Marker Is Taken on Trust

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
