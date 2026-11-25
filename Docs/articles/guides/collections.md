<div class="article">

# Lists, Dictionaries and Spans

A collection of guards is fine to hold and to pass around: copying a list copies no guard. What the analyzer watches is how a guard comes back out. This page shows how to use the elements of a collection without copying them.

### At a Glance

| To | Write |
|---|---|
| Use an element of a list in place | `CollectionsMarshal.AsSpan(list)[i].Dispose()` |
| Loop over the elements | `foreach (ref var guard in CollectionsMarshal.AsSpan(list))` |
| Take a value out of a dictionary | `dictionary.Remove(key, out var guard)` |
| Take a value out of a stack, a queue or a channel | `Pop`, `Dequeue`, `TryDequeue`, `ReadAsync` |
| Release every value of a dictionary | Take a snapshot of the keys, then remove each |

## Lists and Arrays

An element that a list's indexer hands out by value is a copy, and the list still holds the guard. Read it through a span, which gives a reference to the element:

```cs
LockGuard first = guards[0];                              // SCA1030: a copy out of the list
guards[0].Dispose();                                      // SCA1030: disposes a copy

CollectionsMarshal.AsSpan(guards)[0].Dispose();           // OK: in place
ref LockGuard inPlace = ref CollectionsMarshal.AsSpan(guards)[0];
```

A loop works the same way: declare a `ref` variable over a span of the elements.

```cs
foreach (var guard in guards) guard.Dispose();            // SCA1030: each 'guard' is a copy

foreach (ref var guard in CollectionsMarshal.AsSpan(guards)) guard.Dispose();   // OK
foreach (ref var guard in array.AsSpan()) guard.Dispose();                      // OK
```

An array element is used in place directly, and moved out with `NonCopyable.Move(ref array[i])`. Reading one by value is a copy (SCA1001).

> [!TIP]
> A list that the method has just made, and has not passed on, is read like a new value at its last use: `LockGuard first = guards[0];` is fine there. See [Locals That Hold a Container](../rules/last-use.md#locals-that-hold-a-container).

## Dictionaries

Take a value out with the member that removes it. To release every value, take a snapshot of the keys first:

```cs
LockGuard guard = byKey[key];                            // SCA1030: a copy, and the dictionary still has the guard

if (byKey.Remove(key, out var taken)) taken.Dispose();   // OK: taken out

int[] keys = [.. byKey.Keys];                            // OK: the keys only
foreach (int k in keys) { if (byKey.Remove(k, out var each)) each.Dispose(); }
```

Do not call LINQ on `byKey.Keys`. LINQ takes the key collection as a plain `IEnumerable<int>`, but the collection's own type, `Dictionary<int, LockGuard>.KeyCollection`, names `LockGuard`. So `byKey.Keys.ToList()` gets a warning (SCA1023). Copy the keys with `[.. byKey.Keys]`, as above.

Any use of `byKey.Values` gets a warning (SCA1020): it would copy every guard, and it is not on the [symbol list](symbol-list.md).

## Stacks, Queues and Channels

A member that takes the value out for good hands out a new value. A member that only looks hands out a copy:

```cs
LockGuard popped = _stack.Pop();                         // OK: taken out of the stack
LockGuard next = _queue.Dequeue();                       // OK: taken out of the queue
if (_incoming.TryDequeue(out var item)) item.Dispose();  // OK: taken out
if (_incoming.TryPeek(out var peeked)) { }               // SCA1030: the queue still holds the guard
_stack.Peek().Dispose();                                 // SCA1030: disposes the copy that Peek hands out
```

A channel's `ReadAllAsync` and a blocking collection's `GetConsumingEnumerable` take each element out as they hand it over, so a loop over them is clean:

```cs
await foreach (var guard in _channel.Reader.ReadAllAsync())
    guard.Dispose();                                  // OK: each element has left the channel
```

An `async` method that takes the channel as a parameter, and uses it after an `await`, marks the parameter `[Kept]`, or it gets [SCA1032](../diagnostics/sca1032.md). The mark costs the caller nothing, since what a read takes out of a channel is new whatever channel it reads:

```cs
async Task DrainAsync([Kept] ChannelReader<LockGuard> reader)
{
    while (await reader.WaitToReadAsync())            // without [Kept]: SCA1032, since 'reader' is used after an 'await'
    {
        while (reader.TryRead(out var guard))
            guard.Dispose();                          // OK: taken out of the channel
    }
}
```

The usual loop that sends a value: [Sending to a Channel](../rules/framework-symbol-list.md#sending-to-a-channel).

## Collections That Cannot Be Used

A collection that compares or copies its values has no member that is safe to use with a guard, and any use of one gets a warning (SCA1020):

- `HashSet<T>`, `SortedSet<T>` and `SortedDictionary<TKey, TValue>`, which compare what they hold;
- `Lazy<T>`, `ThreadLocal<T>` and `AsyncLocal<T>`, which hand out a copy at every read;
- the immutable collections, LINQ, `Array.Resize` and `Array.Copy`, which copy.

To use a guard in place inside a concurrent dictionary, put the guard in a small class and store that class in the dictionary. What the analyzer knows about each framework collection: [Built-in Framework Symbol List](../rules/framework-symbol-list.md).

## Collections of Your Own

A class that derives from a framework collection, such as `class Guards : List<LockGuard>`, is never a new value to the members that it inherits, because the class's own code decides what it holds. A read through an inherited member is a copy (SCA1030), whatever the object was made from. See [Classes That Derive From a Container on the Symbol List](../rules/listed-members.md#classes-that-derive-from-a-container-on-the-symbol-list).

A generic collection of your own marks its type parameter instead: see [Marking a Type Parameter](marking-a-type-parameter.md).

If a library's generic collection is not marked, you get a warning where you use it. The warning gives the line to add to a symbol list file if the collection makes no copy: see [Symbol List](symbol-list.md).

</div>
