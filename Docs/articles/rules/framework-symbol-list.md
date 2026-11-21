<div class="article">

# Built-in Framework Symbol List

The built-in framework symbol list is the [symbol list](symbol-list-format.md) for .NET's own libraries: it is built into the analyzer and merged with the symbol list files of the project. It has an entry for each member that low-level code reaches for, and a line for each class of the framework that clones itself. Each entry was checked against the runtime's source or its IL. What in the source backs each entry is in [NonCopyableSymbols.evidence.md](https://github.com/Singulink/Singulink.CodeAnalysis/blob/main/Source/Singulink.CodeAnalysis.Analyzers/NonCopyableSymbols.evidence.md), beside the list in the repository.

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

## Classes That Clone Themselves

The built-in framework symbol list marks as `clones` the classes of the framework whose own code clones an object with <xref:System.Object.MemberwiseClone>. A class of the project that derives from one of them, at any depth, gets a warning on each field that holds a non-copyable value (SCA1009), since the clone copies the field:

| Class | What clones |
|---|---|
| <xref:System.Text.Encoding> and every encoding | `Clone`, which the setters of <xref:System.Console.OutputEncoding?displayProperty=nameWithType> and <xref:System.Console.InputEncoding?displayProperty=nameWithType> call as well |
| <xref:System.Globalization.CultureInfo> | `Clone`, and `CultureInfo.ReadOnly` for the culture that it is given |
| <xref:System.Globalization.Calendar> and every calendar | `Clone`, and `Calendar.ReadOnly` for the calendar that it is given |
| <xref:System.Windows.Ink.Stroke> and <xref:System.Windows.Ink.DrawingAttributes> | `Clone`; `Stroke.GetClipResult` and `Stroke.GetEraseResult`, which clone the stroke; `Stroke.Draw` and `Stroke.DrawCore` with drawing attributes, which clone the attributes that they are given |
| <xref:System.Drawing.Printing.PageSettings>, <xref:System.Drawing.Printing.PrinterSettings> and <xref:System.Drawing.Printing.Margins> | `Clone` |
| <xref:System.Xml.Schema.XmlSchemaObject> and every XmlSchema class, and <xref:System.Xml.XmlQualifiedName> | An internal `Clone` that the schema compiler calls |
| <xref:System.Windows.Forms.GiveFeedbackEventArgs> | An internal `Clone` of drag and drop |

```cs
sealed class PinnedEncoding : UTF8Encoding
{
    private GCHandle _pinned;                            // SCA1009: Encoding's Clone copies the handle
    private readonly List<GCHandle> _handles = new();    // OK: the clone shares the list
}
```

One line covers the class that makes the call, every class that derives from it, and the generic classes that derive from it, where a non-copyable type is bound to them (see [SCA1009](../diagnostics/sca1009.md)). So a class of the framework that inherits the call, such as <xref:System.Text.UTF8Encoding> or <xref:System.Globalization.GregorianCalendar>, needs no line of its own for the warning on a field.

Each public or protected member in the table has a `clones` line of its own as well, such as `M:System.Globalization.CultureInfo.Clone` and `M:System.Globalization.CultureInfo.ReadOnly(System.Globalization.CultureInfo)`. So does `PrinterSettings.DefaultPageSettings`, which hands the printer settings on: the page settings that it returns refer back to them. No line of a symbol list file can vouch for any of these members: see [Clones](symbol-list-format.md#clones).

The lines of every class in the table but the Xml classes say `clones=complete`: their `clones` lines name every member that clones the object or hands it on. So do the lines of the 19 public classes of the framework that derive from them (four encodings and 15 calendars). Any other member of these classes, such as `culture.Name`, gets no warning where it is used on an object whose class binds a non-copyable type, unless its signature shows that it can hand the object on. Five members have such a signature, and a line with no marker that says that they never hand back the object itself:

- `CultureInfo.Parent`;
- `CultureInfo.GetFormat`;
- `CultureInfo.GetConsoleFallbackUICulture`;
- the `GetPropertyData` of `Stroke` and of `DrawingAttributes`, which returns what `AddPropertyData` was given.

The events of `Stroke` and `DrawingAttributes` hand the object to their handlers as the sender, so subscribing to one on such an object gets a warning.

The analyzer treats the Xml classes cautiously. The test that backs the list (see below) leaves their members out, so <xref:System.Xml.Schema.XmlSchemaObject>, the XmlSchema classes and <xref:System.Xml.XmlQualifiedName> are not marked `clones=complete`. The analyzer counts every one of their members as one that can clone, so you get a warning for any use of one on an object whose class binds a non-copyable type. That means more warnings for these classes, not a gap.

Where you can vouch for their members, a symbol list file of your own can mark these classes `clones=complete`. The line `T:System.Xml.Schema.XmlSchemaObject;clones=complete` covers <xref:System.Xml.Schema.XmlSchemaObject> and every XmlSchema class, since they are in the same assembly. <xref:System.Xml.XmlQualifiedName> takes a line of its own.

<xref:System.Windows.Forms.GiveFeedbackEventArgs> clones only through an internal member, has no member line, and is marked `clones=complete`.

**A test keeps the list complete.** It reads the IL of every assembly of the shared frameworks of .NET 10 and .NET 11:

- It finds each class that clones `this`, or the argument of a static member of the class, and that another assembly can derive from. It fails for one that has no line, of its own or of a base class.
- It follows the calls of the framework from the members of these classes to the ones that clone. It fails for a public or protected member that clones its object or one that it is given, through other members included, and has no line.
- It checks that none of the five members with a line with no marker can return its object.
- It follows where the object of each member can go, for every class whose line says `clones=complete` and the classes that derive from it. A member, or a constructor, that stores it, captures it in a delegate, or gives it to code that an application can supply, has a `clones` line or an entry in the evidence that says why it hands nothing on. In this check, the fallbacks of an encoding, its encoders and decoders, and the helpers of a calendar and of a stroke count as code of the class: an application's own fallback or encoder does receive the encoding.
- It fails where a line says `clones=complete` for a class that the check above does not cover. It also fails where a public class derives from a class whose line says `clones=complete` and has no such line of its own.

Two classes need no line:

| Class | Why it needs no line |
|---|---|
| <xref:System.Delegate> | No class of C# can derive from it |
| <xref:System.MarshalByRefObject> | It clones only where a derived class calls `MemberwiseClone(bool)`, which gets SCA1005 there |

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
