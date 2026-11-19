<div class="article">

# Built-in Framework Symbol List

The built-in framework symbol list is the [symbol list](symbol-list-format.md) for .NET's own libraries: it is built into the analyzer and merged with the symbol list files of the project. It has an entry for each member that low-level code reaches for, and each entry was checked against the runtime's source.

This page says what is on it, and what cannot be. The [symbol list files](symbol-list-format.md) that a project reads, its own and those that come with a library, are merged with it, and none of their lines can loosen one of its entries, except `copyable`.

### At a Glance

| A member that | Is on the built-in framework symbol list as |
|---|---|
| Takes a value out of its collection | Handing out a new value |
| Reads a value that the collection still holds | No marker: a copy on a stored collection (SCA1030) |
| Uses a container or a value of its own non-copyable type during the call only | `borrows` |
| Hands out a sequence that takes each element out of its source | `drains` |
| Compares values, or copies them | Not on the framework symbol list |

## Members of the Built-in Non-copyable Types

The built-in framework symbol list marks as `borrows` the members of its own non-copyable types that take the type by value and only read it:

- `ToIntPtr` and `Equals` of the four handle types;
- the comparison operators and the conversion to <xref:System.IntPtr> of <xref:System.Runtime.InteropServices.GCHandle>;
- `GetAddressOfArrayData` and `GetAddressOfStringData`;
- the comparisons of <xref:System.Threading.AsyncFlowControl>.

The constructor of <xref:System.Buffers.MemoryHandle> keeps the <xref:System.Runtime.InteropServices.GCHandle> that it is given, so a stored handle that is passed to it still gets a warning.

## Members That Only Use a Container

The built-in framework symbol list also marks as `borrows` its members that take a container through a parameter over their type parameter and only use it during the call:

- <xref:System.Array.Reverse*?displayProperty=nameWithType>, <xref:System.MemoryExtensions.Reverse*?displayProperty=nameWithType> and `Overlaps`;
- `Equals` of <xref:System.Memory`1>, <xref:System.ReadOnlyMemory`1> and <xref:System.ArraySegment`1>;
- the comparison operators of the spans and of <xref:System.ArraySegment`1>.

<xref:System.Buffers.ArrayPool`1.Return*?displayProperty=nameWithType> and <xref:System.Threading.Tasks.Task.Run*?displayProperty=nameWithType> keep what they are given and have no marker. A member that hands the container back, as `AsSpan`, `AsMemory` and `TryGetArray` do, has none either: what it hands back is followed.

## Channels and Concurrent Collections

The built-in framework symbol list has the types that hand a value over between tasks and threads: <xref:System.Threading.Channels.Channel`1> with its <xref:System.Threading.Channels.ChannelReader`1> and <xref:System.Threading.Channels.ChannelWriter`1>, <xref:System.Collections.Concurrent.ConcurrentQueue`1>, <xref:System.Collections.Concurrent.ConcurrentStack`1>, <xref:System.Collections.Concurrent.ConcurrentBag`1>, <xref:System.Collections.Concurrent.ConcurrentDictionary`2> and <xref:System.Collections.Concurrent.BlockingCollection`1>.

| Member | On the framework symbol list as |
|---|---|
| A write | Moving the value in |
| A read that takes the value out: `TryRead`, `ReadAsync`, `TryDequeue`, `TryPop`, `TryTake`, `Take`, `TryRemove` | Handing out a new value |
| What hands out a value that the collection still holds: `TryPeek`, `TryGetValue`, `GetOrAdd`, the indexer, an enumerator | No marker: a copy on a stored collection (SCA1030) |
| What copies every element (`ToArray`, `CopyTo`) and what compares values (`TryUpdate`, `AddOrUpdate`) | Not on the framework symbol list |

#### Sequences That Drain

A sequence that takes each element out as it is enumerated is marked `drains`: <xref:System.Threading.Channels.ChannelReader`1.ReadAllAsync*?displayProperty=nameWithType> and <xref:System.Collections.Concurrent.BlockingCollection`1.GetConsumingEnumerable*?displayProperty=nameWithType>. So both loops are clean, even on a stored channel or a stored collection:

```cs
await foreach (var guard in _channel.Reader.ReadAllAsync())
    guard.Dispose();                                  // OK: each element has left the channel

foreach (var guard in _pending.GetConsumingEnumerable())
    guard.Dispose();                                  // OK
```

- **The same sequence behind `ConfigureAwait` or `WithCancellation`** is the same sequence, so `await foreach (var guard in reader.ReadAllAsync(ct).ConfigureAwait(false))` is clean as well.
- **The marker describes the sequence as the call returns it.** An enumerator of the sequence that you drive by hand holds its current element until it moves on, so a second read of `Current` is a copy.
- **A sequence that is kept in a field** is a stored container like any other.

## Dictionary Keys

<xref:System.Collections.Generic.Dictionary`2.Keys?displayProperty=nameWithType> is on the built-in framework symbol list with its collection and its enumerator, which read no value. A conversion of the key collection to `IEnumerable<TKey>`, such as the one that LINQ makes, still gets a warning (SCA1023), since the type of the collection names the value type. How to release every value of a dictionary: [Dictionaries](../guides/collections.md#dictionaries).

## Collections, Tasks and Views

| Type | On the built-in framework symbol list |
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

## What Cannot Be on the Built-in Framework Symbol List

These cannot be on the built-in framework symbol list, by design:

| Type or member | Why |
|---|---|
| <xref:System.Collections.Generic.HashSet`1>, <xref:System.Collections.Generic.SortedSet`1>, <xref:System.Collections.Generic.SortedDictionary`2> | They compare what they hold. The last gives each pair that a lookup passes, value included, to its comparer by value |
| <xref:System.Lazy`1>, <xref:System.Threading.ThreadLocal`1>, <xref:System.Threading.AsyncLocal`1> | They hand out a copy at every read and have no access by reference |
| The immutable collections, LINQ, <xref:System.Array.Resize*?displayProperty=nameWithType>, <xref:System.Array.Copy*?displayProperty=nameWithType>, <xref:System.Collections.Generic.Dictionary`2.Values?displayProperty=nameWithType>, the making of a frozen dictionary | They copy |
| <xref:System.Runtime.InteropServices.CollectionsMarshal.SetCount*?displayProperty=nameWithType> | It can turn a removed element back into an element of the list |

> [!TIP]
> To use a guard in place inside a concurrent dictionary, put the guard in a small class and store that class in the dictionary.

</div>
