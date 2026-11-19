<div class="article">

# Symbol List Format

A symbol list file tells the analyzer what it cannot see for itself in code that you cannot mark. This page gives the exact format, every marker, and how entries combine. [Symbol List](../guides/symbol-list.md) shows where the files go and how to use them, and why most projects never need one.

### At a Glance

- **One entry per line:** a [documentation comment ID](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/#id-strings), with an optional marker after a semicolon, or the simple name of an assembly after `A:` with the marker `trusted`.
- **Comments:** a line that starts with `#`, and the rest of a line after white space and `#`.
- **The ID to add** is in the message of SCA1020.
- **Entries are exact per overload.** There is no entry for every member of a type, and no wildcard.
- **The files:** the project's own symbol list files and those that come with a library are read alike and merged with the [built-in framework symbol list](framework-symbol-list.md).
- **Where two entries disagree,** the one that claims less applies, even between a project's file and a library's. The one exception: `copyable` overrides the framework symbol list.
- **A line of a symbol list file that cannot be parsed,** and a `noncopyable`, `copyable` or `trusted` line that has no effect, gets a warning (SCA1042). The analyzer's own tests check the framework symbol list, so it never gets one.

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

An assembly marked `trusted` was not built with the analyzer, and its code makes no copy of the non-copyable values that it gets at. That holds even where its code runs behind a type of checked code or of the framework. The line turns off SCA1025 for the assembly and changes nothing else: its members stay unchecked code for every other rule. The name is matched without regard to case, as the runtime binds it. See [SCA1025](../diagnostics/sca1025.md).

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

- **A `noncopyable` line that names no type** gets a warning because a mistyped ID would leave the type copyable in a build that looks clean. A project that does not reference the type should take the line out of its file.
- **A `copyable` line has no effect** in the cases that [Copyable](#copyable) gives.
- **A `trusted` line has no effect** for an assembly that the project does not reference, for the project's own, and for one that was built with the analyzer. Nor does it for an assembly that references no assembly which declares a non-copyable type.

A line cannot be parsed if it has:

- an ID that cannot be a documentation comment ID, such as one with a parenthesis that is not closed;
- a `for=` that names a type parameter which the member does not have, or one that fits more than one type parameter of the member and of the types that contain it;
- a marker on something that it does not fit (see the table below);
- a marker other than `trusted` on an assembly, or `trusted` on anything but an assembly;
- an assembly given by a display name with a version, or by a file name.

| Marker | Fits only |
|---|---|
| `view`, `noncopyable`, `copyable` | A struct |
| `erasable` | A generic class or interface |
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

So a file can add entries and can tighten a built-in one, but cannot loosen it. The one line that loosens is `copyable`.

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
