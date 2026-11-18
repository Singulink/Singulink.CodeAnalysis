<div class="article">

# The Symbol List

The symbol list tells the analyzer what it cannot see for itself in code that you cannot mark. The members that it vouches for are called the safe list in the messages and in this documentation.

## List Files

The list names the members of unmarked generics that are safe to use with a non-copyable type, the members that a stored value may be passed
to by value, the structs that are views, the structs that are non-copyable or copyable and the types that are erasable. The analyzer ships a
list for the framework. A project adds to it with files named `NonCopyableSymbols.txt` or `NonCopyableSymbols.<name>.txt`, whatever the case of
the name. **Such a file in the folder of the project is read with nothing more written.** A file in another folder is added as an additional
file:

```xml
<ItemGroup>
  <AdditionalFiles Include="Lists\NonCopyableSymbols.ThirdParty.txt" />
</ItemGroup>
```

A file of the project's folder that the project adds itself as well is read once.

The second form is for a file that is named after what it describes, and it is the form for
[a list that a library ships](libraries.md#shipping-a-list-with-a-library).

## The Format

A file has one [documentation comment ID](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/#id-strings) per line, with an
optional marker after a semicolon. A line that starts with `#` is a comment, and so is the rest of a line after white space and `#`. The message of
`SCA1020` gives the ID to add.

```
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

# copyable: a struct that the built-in list or a library makes non-copyable, and that this project copies on purpose
T:System.Runtime.InteropServices.GCHandle;copyable

# erasable: no base type or interface of the type hands out a value of its type arguments
T:ThirdParty.Promise`1;erasable
```

| Entry | Marker | Meaning |
|---|---|---|
| A member (`M:`, `P:`, `E:`) | none | Safe to use. What it hands out is as new as what it was called on and with. A property covers its getter and setter. |
| A member | `new` | Safe to use. What it hands out is a new value. |
| A member | `borrows` | A stored value of a non-copyable type may be passed to it by value, and a container may be passed to a parameter of it whose type mentions a type parameter that is not marked: without the marker such a parameter is [taken to keep the container](../concepts/keeping-containers.md). The member uses what it is given during the call and keeps nothing of it. Everything else about the call stays as it is. It is the one marker that is of use on a member that is not generic. |
| A method that returns a sequence (<xref:System.Collections.Generic.IEnumerable`1>, <xref:System.Collections.Generic.IAsyncEnumerable`1>) | `drains` | Safe to use, and the sequence takes each element out of its source as it hands it out, so nothing hands an element out twice. The sequence is a new container, whatever the method was called on: what a `foreach` or an `await foreach` over it hands out is new, and a second enumeration takes further elements, never the same ones. As with `new`, that is said of values of a non-copyable type: a container that is taken out is a reference that whoever put it in may still hold. A method that hands out its elements and keeps them, as an ordinary enumeration does, must not have the marker. |
| An instance property of a class, of type <xref:System.Threading.Tasks.Task`1> | `owned` | Safe to use, and the task belongs to the object alone: it is made with the object, no constructor takes it from somewhere else, it is never replaced, and no other member hands it or its result out. Its one read on an object that the method has just made is a new value, as for <xref:System.Threading.Tasks.TaskCompletionSource`1.Task?displayProperty=nameWithType>, the one built-in entry. See [What a listed member hands out](../concepts/listed-members.md). |
| A member | `for=T1,T2` | After the marker, if any. The entry vouches only for these type parameters of the member or of its containing types. With a non-copyable type bound to another one, the member counts as not listed. |
| A struct (`T:`) | `view` | Copying the struct copies no elements, as for <xref:System.Span`1>. It is a container and not a non-copyable type. |
| A struct (`T:`) | `noncopyable` | The struct is a non-copyable type. |
| A struct (`T:`) | `copyable` | The struct is an ordinary struct for the project, though the built-in list or an attribute in a referenced assembly makes it a non-copyable type: one of the ten that the project copies on purpose, or a struct that a library marked, for this analyzer or, with an attribute of another namespace, for another one. Its fields, the generic structs over it, its containers and a marked type parameter that it is bound to all follow. It is the project's decision, and nothing that the analyzer guarantees holds for the type. |
| A generic class or interface (`T:`) | `erasable` | No base type or interface of the type hands out a value of its type arguments, so a container of the type may be converted to one of them. See [Variance and erasable types](../concepts/variance-and-erasable-types.md). |

## Rules for Entries

- Entries are exact per overload. There is no entry for every member of a type and no wildcard.
- A member of an extension block is listed under the ID of its implementation method: the static method that the compiler makes for
  it in the class that the block is in, with the receiver as its first parameter and the type parameters of the block before its
  own. For `extension<T>(Bag<T> bag) { public void TouchWith<U>(ref U other) { } }` in `BagExtensions` that is
  ``M:BagExtensions.TouchWith``2(Bag{``0},``1@)``, and `for=` names `T` or `U` as they are written. A property is listed under the
  method of its getter (`get_Size`), or of its setter if it has no getter. The ID that the compiler gives the member inside the
  block is accepted as well, but it carries a generated name that changes with the declaration of the receiver. A finding that asks
  for an entry names the implementation method.
- A member may be listed if it makes no copy of a non-copyable value other than the one it hands out. A member that moves values inside its
  container, like a list that grows, qualifies. One that leaves two live instances, like `ToArray` or `CopyTo`, does not.
- An entry that names something the project does not reference is ignored, so one file can serve several projects. The one exception is a
  `noncopyable` line, which is reported (`SCA1042`) where it names no type: a mistyped ID there would leave the type copyable in a build
  that looks clean. A project that does not reference the type takes the line out of its file. A line that cannot be parsed is
  reported (`SCA1042`) and then ignored. That includes an ID that cannot be a documentation comment ID, such as one with a parenthesis that
  is not closed, a `for=` that names a type parameter which the member does not have, or one that fits more than one type parameter of
  the member and of the types that contain it, and a marker on something that it does not fit:
  `view`, `noncopyable` or `copyable` on anything but a struct, `erasable` on anything but a generic class or interface, `new` or `borrows` on a type,
  `drains` on anything but a method whose result is an <xref:System.Collections.Generic.IEnumerable`1> or an <xref:System.Collections.Generic.IAsyncEnumerable`1>,
  `owned` on anything but an instance property of a class whose type is <xref:System.Threading.Tasks.Task`1>.
- A `copyable` line is reported (`SCA1042`) where it has no effect: on anything but a struct, where it names no type, for a struct that the
  project declares itself, whose `[NonCopyable]` attribute is what to take off, and for a struct that nothing makes non-copyable by itself,
  neither the built-in list nor an attribute, such as <xref:System.Threading.Tasks.ValueTask`1>, which is non-copyable only through what it holds.
- Where two entries for the same ID disagree, the one that claims less applies, whether they are in two files of the project or in a file
  and the built-in list: `noncopyable` wins over `view`, no marker wins over `new`, over `borrows`, over `drains` and over `owned`, and of two `for=`
  limits only the type parameters that both name are covered. So a file can add entries and can tighten a built-in one, but cannot
  loosen it. The one line that loosens is `copyable`, which overrides the built-in list, since that is what it is for; where one file of
  the project says `copyable` and another `noncopyable` of the same type, `noncopyable` applies.
- The list is for code you cannot change. Your own generics mark their type parameters instead.
- An `owned` entry is a promise about the whole type, and the analyzer takes it on trust: the task is made with the object, no
  constructor takes it from somewhere else, and nothing but the property hands it out. The analyzer checks one part of it, that the
  object is created with no argument that carries a non-copyable type, and that check sees nothing where the type is not generic:
  `new Promise(shared)` with a constructor that takes over the task of another object gives the same task to every caller. A struct
  cannot keep the promise at all, because a copy of it shares the task and a call on it can replace it, so the marker is not accepted
  there.

## What Is Built In

- The built-in list marks as `borrows` the members of its own non-copyable types that take the type by value and only read it: `ToIntPtr` and
  `Equals` of the four handle types, the comparison operators and the conversion to <xref:System.IntPtr> of <xref:System.Runtime.InteropServices.GCHandle>, `GetAddressOfArrayData` and
  `GetAddressOfStringData`, and the comparisons of <xref:System.Threading.AsyncFlowControl>. The constructor of <xref:System.Buffers.MemoryHandle> keeps the <xref:System.Runtime.InteropServices.GCHandle> that it is
  given, so a stored handle that is passed to it stays reported.
- The built-in list also marks as `borrows` its members that take a container through a parameter over their type parameter and only
  use it during the call: <xref:System.Array.Reverse*?displayProperty=nameWithType>, <xref:System.MemoryExtensions.Reverse*?displayProperty=nameWithType> and `Overlaps`, `Equals` of <xref:System.Memory`1>, <xref:System.ReadOnlyMemory`1> and
  <xref:System.ArraySegment`1>, and the comparison operators of the spans and of <xref:System.ArraySegment`1>. <xref:System.Buffers.ArrayPool`1.Return*?displayProperty=nameWithType> and <xref:System.Threading.Tasks.Task.Run*?displayProperty=nameWithType> keep what
  they are given and have no marker. A member that hands the container back, as `AsSpan`, `AsMemory` and `TryGetArray` do, has none
  either: what it hands back is followed.
- The built-in list has the types that hand a value over between tasks and threads: <xref:System.Threading.Channels.Channel`1> with its <xref:System.Threading.Channels.ChannelReader`1> and
  <xref:System.Threading.Channels.ChannelWriter`1>, <xref:System.Collections.Concurrent.ConcurrentQueue`1>, <xref:System.Collections.Concurrent.ConcurrentStack`1>, <xref:System.Collections.Concurrent.ConcurrentBag`1>, <xref:System.Collections.Concurrent.ConcurrentDictionary`2> and
  <xref:System.Collections.Concurrent.BlockingCollection`1>. A write moves the value in. A read that takes it out gives a new value: `TryRead`, `ReadAsync`,
  `TryDequeue`, `TryPop`, `TryTake`, `Take` and `TryRemove`. What hands out a value that the collection still holds is listed with
  no marker, and is a copy on a stored collection (`SCA1030`): `TryPeek`, `TryGetValue`, `GetOrAdd`, the indexer and an enumerator.
  Not on the list is what copies every element (`ToArray`, `CopyTo`) and what compares values (`TryUpdate`, `AddOrUpdate`).
- A sequence that takes each element out as it is enumerated is marked `drains`: <xref:System.Threading.Channels.ChannelReader`1.ReadAllAsync*?displayProperty=nameWithType> and
  <xref:System.Collections.Concurrent.BlockingCollection`1.GetConsumingEnumerable*?displayProperty=nameWithType>. So both loops are clean, on a stored channel and a stored collection as well:

  ```cs
  await foreach (var guard in _channel.Reader.ReadAllAsync())
      guard.Dispose();                                  // OK: each element has left the channel

  foreach (var guard in _pending.GetConsumingEnumerable())
      guard.Dispose();                                  // OK
  ```

  The same sequence behind `ConfigureAwait` or `WithCancellation` is the same sequence, so
  `await foreach (var guard in reader.ReadAllAsync(ct).ConfigureAwait(false))` is clean as well.

  The marker speaks of the sequence as the call returns it. An enumerator of it that is used by hand holds its current element until it
  moves on, so a second read of `Current` is a copy, and a sequence that is kept in a field is a stored container like any other.
- <xref:System.Collections.Generic.Dictionary`2.Keys?displayProperty=nameWithType> is on the list with its collection and its enumerator, which read no value. To release every value
  of a dictionary, take a snapshot of the keys with a spread and remove each: `int[] keys = [.. held.Keys];`. `held.Keys.ToList()`
  converts the key collection to `IEnumerable<TKey>`, which is still reported (`SCA1023`), since the type of the collection names
  the value type.

- The built-in list covers what low-level code reaches for, each member read in the runtime source. A read that takes a value out
gives a new value; a read of what the collection still holds is a copy on a stored collection; a member that compares values or
copies them is left out.

| Type | On the list |
|---|---|
| <xref:System.Collections.Generic.List`1>, <xref:System.Collections.Generic.Queue`1>, <xref:System.Collections.Generic.Stack`1> | adding, taking out, peeking, enumerating and the capacity; for a list also removing a range and reversing in place |
| <xref:System.Collections.Generic.Dictionary`2> | for the value: the constructors with a comparer, lookups, the capacity, `Keys`, and the alternate lookup |
| <xref:System.Collections.Generic.PriorityQueue`2> | for the element: everything but what enqueues a sequence or removes by comparing elements |
| <xref:System.Collections.Generic.LinkedList`1>, <xref:System.Collections.Generic.LinkedListNode`1> | adding, removing and walking nodes, `ValueRef`, and `Value` as a read by value |
| <xref:System.Collections.Generic.SortedList`2> | for the value: everything but `ContainsValue`, `IndexOfValue`, `Values` and the copying constructors |
| <xref:System.Collections.Frozen.FrozenDictionary`2> | for the value: the lookups, `GetValueRefOrNullRef` and the alternate lookup |
| <xref:System.Collections.Concurrent.ConcurrentDictionary`2>, the channels, the concurrent collections | as above |
| <xref:System.Runtime.CompilerServices.StrongBox`1> | the constructors; its `Value` is a field, which is reached in place |
| <xref:System.Threading.Tasks.Task`1>, <xref:System.Threading.Tasks.TaskFactory`1>, <xref:System.Threading.Tasks.ValueTask`1>, <xref:System.Threading.Tasks.TaskCompletionSource`1> | making and completing a task with one result, and reading it |
| <xref:System.Span`1>, <xref:System.Memory`1>, <xref:System.ArraySegment`1>, <xref:System.Buffers.ReadOnlySequence`1>, <xref:System.Buffers.IBufferWriter`1>, <xref:System.Buffers.ArrayBufferWriter`1>, <xref:System.Buffers.MemoryManager`1> | the views and the members that hand out a view |

## What Cannot Be Listed

What cannot be listed, by design: <xref:System.Collections.Generic.HashSet`1>, <xref:System.Collections.Generic.SortedSet`1> and <xref:System.Collections.Generic.SortedDictionary`2> compare what they hold (the
last gives each pair that a lookup passes, value included, to its comparer by value); <xref:System.Lazy`1>, <xref:System.Threading.ThreadLocal`1> and
<xref:System.Threading.AsyncLocal`1> hand out a copy at every read and have no access by reference; the immutable collections, LINQ, <xref:System.Array.Resize*?displayProperty=nameWithType>,
<xref:System.Array.Copy*?displayProperty=nameWithType>, <xref:System.Collections.Generic.Dictionary`2.Values?displayProperty=nameWithType> and the making of a frozen dictionary copy; and <xref:System.Runtime.InteropServices.CollectionsMarshal.SetCount*?displayProperty=nameWithType>,
which can make an element again of what was removed. A guard that must be used in place inside a concurrent dictionary goes into a
small class that the dictionary holds.

## The Built-in Non-copyable Types

The ten framework structs are types that a project may copy on purpose. **They are on in a project that references one of the two packages
itself, with a `PackageReference`, and off in a project that only gets the analyzers through a library.** A library that marks its guards
brings the analyzers to everyone who uses it, so that they cannot copy those guards, and that does not bring findings about their own
<xref:System.Runtime.InteropServices.GCHandle> code with it. The package decides it where the project has not set the MSBuild property `NonCopyableIncludeBuiltInTypes`; a project
that sets it keeps its value, in the project file or in a `Directory.Build.props`:

```xml
<PropertyGroup>
  <!-- Off in a project that references a package; on in one that only consumes a library. -->
  <NonCopyableIncludeBuiltInTypes>false</NonCopyableIncludeBuiltInTypes>
</PropertyGroup>
```

Only the value `true` turns them on, and a build that has no value, such as one in which the build files of the package did not run, has
them off. With central package management a `PackageReference` with no version counts, and so does a `GlobalPackageReference`, which
becomes one; a version that is only pinned for a transitive reference does not. Structs marked `[NonCopyable]`, the `noncopyable` entries
of the project's own list files, generic structs over a non-copyable type and every other entry of the built-in list are not affected. To
get some of the ten back where they are off, name them in a list file of the project (`T:System.Threading.SpinLock;noncopyable`); to drop
one of them where they are on, take it out with a `copyable` line (`T:System.Runtime.InteropServices.GCHandle;copyable`).

</div>
