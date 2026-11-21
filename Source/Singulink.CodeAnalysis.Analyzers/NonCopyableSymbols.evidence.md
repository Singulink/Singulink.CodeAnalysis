# Evidence for the Framework Symbol List

This file backs the built-in framework symbol list, [NonCopyableSymbols.txt](NonCopyableSymbols.txt), which is built into the
analyzer. For each entry it says what in the .NET runtime's source shows that the member makes no copy, and for many members that
are not on the list, why they are not. The [Built-in Framework Symbol List](../../Docs/articles/rules/framework-symbol-list.md) article says in plain
words what is on the list, and the [Symbol List Format](../../Docs/articles/rules/symbol-list-format.md) article says what each marker means.

The file grew with the list. Sections 1 to 6 are the first draft of the list and the questions that it raised. Section 7 records
what changed after the draft, section 8 a removal that was considered and not made, and sections 9 to 13 later additions, each of
which read the members that it added. Where two sections disagree, the later one holds. An entry that is added to the list or changed gets its evidence here.

## 1. Source

- **Runtime source read:** dotnet/runtime `d757c03be2c39968bca1db2ce9349fce8f1463a3` (branch `main`, 18 July 2025, "Add some additional Tensor APIs (#117739)"). That is the .NET 10 development line, before the .NET 10 release. Everything listed from it also exists in the released .NET 10 and .NET 11 reference assemblies (section 5), so nothing on the list exists only in unreleased code.
- **Path abbreviations in the tables:**
  - `CL/` = `src/libraries/System.Private.CoreLib/src/System/`
  - `CLR/` = `src/coreclr/System.Private.CoreLib/src/System/`
  - `Lib/` = `src/libraries/`
- **Coverage.** The draft covered these member by member: the awaiting group, spans and memory, arrays, the marshal classes, `Unsafe`, the pools, `GC`, `Interlocked` and `Volatile`. It covered these **only at a basic level, on purpose**: construct, add, remove, count, clear, indexer and enumeration.
  - `List<T>`, `Stack<T>`, `Queue<T>`, `Dictionary<TKey, TValue>`;
  - the generic collection interfaces;
  - `Nullable<T>`, `KeyValuePair<TKey, TValue>`, `ValueTuple`;
  - the opaque sinks.

  **Left out on purpose:** `LinkedList<T>`, the concurrent collections, `ReadOnlySequence<T>`'s own members, `ImmutableArray<T>`'s members (it has only its `view` entry), `ManualResetValueTaskSourceCore<T>`'s members (only its `noncopyable` entry), `ContinueWith`, `TaskFactory` and `PoolingAsyncValueTaskMethodBuilder<T>`. A use of these types with a non-copyable type got a warning until someone listed it, and later sections listed several of them.

## 2. How the criterion was applied

- **Listed:** the member makes no copy of a `T` value other than what it hands out, by return value, `out` parameter or a container that it returns.
- **Rejected:** members that copy into a second place that stays reachable, that pass a `T` by value to user code (comparers, `Equals`, `GetHashCode`, `ToString`, delegates), or that erase the type through a reinterpreting cast or a pointer.
- **Internal copies that only end up in what is handed out are treated as part of the hand-out.** `List<T>.Enumerator.MoveNext` copies into `_current`, and `Current` hands that copy out; `ValueTask<T>.ConfigureAwait` copies the whole value task into the awaitable. The rules' own examples need this reading: `foreach (Guard item in CreateGuards())` and `await EnterAsync().ConfigureAwait(false)` are meant to be OK. Section 3 marks each member where this reading carries the weight.
- **Rule of thumb that separates `ToArray` from `AsTask`:** a second instance is acceptable only if user code can reach it solely through a by-value read, which then gets a warning on a stored receiver (see [What a Member on the Symbol List Hands Out](../../Docs/articles/rules/listed-members.md)). A result that gives in-place (`ref`) access to the second instance, such as an array, is never acceptable.

## 3. Points that needed a decision

These were the points of the draft that needed a decision. The verdicts here and in section 4 are the draft's; section 7 says how
several of them were decided.

1. **`Task<T>.WaitAsync` (all five overloads) is rejected.** It is listed now: see section 7.
   - Unless it can return `this`, it returns a `CancellationPromise<T>` (`CL/Threading/Tasks/Future.cs:599-616`). On completion that promise does `TrySetResult(taskTResult.Result)` (`CL/Threading/Tasks/Task.cs:2925`), so the original task and the promise then both hold the result.
   - Under the rule of thumb in section 2 this could be listed: the original task gives access only by value. The draft rejected it under "when in doubt" and because it leaves two live instances.
   - The non-generic `Task.WaitAsync` overloads are listed: with `TResult = VoidTaskResult` they copy nothing (`Task.cs:2849`).
2. **Generic `Task.WhenAll<TResult>` is rejected.**
   - It gathers every result into a new `TResult[]` while the tasks keep theirs (`Task.cs:6252-6380`), and an array gives in-place access to the copies.
   - `await Task.WhenAll(first, second)` with two `Task<Guard>` arguments binds to `WhenAll<Guard>(params Task<Guard>[])`, not to `WhenAll(params Task[])`. It reaches the non-generic overload only with a cast to `Task`.
3. **Generic `Task.WhenAny<TResult>` and `WhenEach<TResult>` are rejected.** `WhenAny<TResult>` is listed now: see section 7.
   - They copy nothing, but they hand back one of the argument tasks. A `params` array built from stored `Task<Guard>` locals is a creation expression, so the analyzer may treat it as new. The returned task would then count as new while the local still reaches the same task: a by-value read through both is a duplicate.
   - The non-generic overloads are listed: what they return is a `Task`, and reading a result from it needs a cast, which gets a warning.
4. **`Task.FromResult<T>` is listed, but it can hand out a shared cached task.**
   - For a reference-free struct of 1, 2, 4, 8 or 16 bytes whose bits are all zero, it returns `Task<T>.s_defaultResultTask` (`Task.cs:5394-5430`). The same goes for a `null` `Nullable<T>`.
   - Several callers then read "new" results from one task. Each read is a by-value copy of `default`, which is no different from writing `default`, so it is listed.
   - `AsyncTaskMethodBuilder<T>.SetResult` goes through this path (`CL/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs:473`).
5. **`ArrayPool<T>.Rent` and `GC.AllocateUninitializedArray<T>` are listed as `new`.**
   - `SharedArrayPool<T>.Rent` hands out pooled arrays with whatever the last renter left (`CL/Buffers/SharedArrayPool.cs:71,89`). It allocates new arrays with `GC.AllocateUninitializedArray` (`SharedArrayPool.cs:110`), which does not zero a reference-free `T` (`CLR/GC.CoreCLR.cs:798`).
   - A by-value read of such an element is "new" and can be the bits of an abandoned value. This is treated as misuse of those APIs (the contents are unspecified until written), not as a copy.
6. **`Dictionary<TKey, TValue>` entries cannot be limited to `TValue`.**
   - An entry covers every type parameter of the member. `Add`, `TryAdd`, `Remove`, `TryGetValue`, the indexer and `ContainsKey` pass the key by value to `IEqualityComparer<TKey>`, and so do the `CollectionsMarshal` dictionary entries.
   - With a non-copyable `TKey` these entries would let copies through. The draft listed them, judged for a non-copyable `TValue`.
   - This was settled with a qualifier in the list format: the entries are limited with `for=TValue` (section 7).
7. **The async method builders are listed.** The entries were removed later: see section 7.
   - `AsyncValueTaskMethodBuilder<T>.Task` returns `new ValueTask<TResult>(_result)` and leaves `_result` in the builder (`CL/Runtime/CompilerServices/AsyncValueTaskMethodBuilderT.cs:70`). The builder is a private field of the compiler's state machine and is dropped with it, so no user code can reach the second instance.
   - `AwaitOnCompleted`/`AwaitUnsafeOnCompleted` copy the state machine into a heap box. That is the compiler's own move.
   - Whether the analyzer looks at builder calls at all was open (section 6).
8. **New `noncopyable` entries beyond `SpinLock` and `GCHandle`.** Each owns something that a copy would release twice:
   - `GCHandle<T>`, `PinnedGCHandle<T>` and `WeakGCHandle<T>` are .NET 10 types; their `Dispose` frees the handle (`CL/Runtime/InteropServices/GCHandle.T.cs:89`).
   - `Lock.Scope`: `Dispose` exits the lock (`CL/Threading/Lock.cs:137-143`).
   - `MemoryHandle`: `Dispose` frees a `GCHandle` and unpins (`CL/Buffers/MemoryHandle.cs:40-54`).
   - `DefaultInterpolatedStringHandler`: `ToStringAndClear` returns its rented buffer to the pool (`CL/Runtime/CompilerServices/DefaultInterpolatedStringHandler.cs:110-136`).
   - `AsyncFlowControl`: `Dispose`/`Undo` restores the flow (`CL/Threading/ExecutionContext.cs:550`).
   - `ManualResetValueTaskSourceCore<TResult>`: a mutable struct holding `_result` and the continuation, meant to live in one field (`CL/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs:34,65`).
9. **The line for `Unsafe` and the marshal classes.**
   - Listed: members that only compute or compare references, or that make a typed reference with no copy: `Add`, `Subtract`, `AreSame`, `AsRef(ref)`, `AsRef(void*)`, `NullRef`, `GetReference`, `CreateSpan`.
   - Rejected: members that copy (`Read*`, `Write*`, `Copy`, `BitCast`), that reinterpret to another type (`As<TFrom, TTo>`, `MemoryMarshal.Cast`/`AsBytes`/`AsRef<T>(Span<byte>)`), or that give out a pointer (`AsPointer`, `Marshal.UnsafeAddrOfPinnedArrayElement`). Those lose the type, as a conversion to a type that hides it does (see [Containers and Conversions](../../Docs/articles/rules/conversions.md)).
   - `AsRef<T>(void*)` is listed. It creates a typed reference over untyped memory and copies nothing, but it is the one that lets native memory hold a guard.
10. **`CollectionsMarshal.SetCount` is rejected, because of stale slots.**
    - For a reference-free `T`, `List<T>.RemoveAt` and `Clear` do not clear the vacated slots (`CL/Collections/Generic/List.cs:1001-1019`, `320-340`).
    - `RemoveAt` shifts elements down, so the slot past the end keeps a bitwise duplicate of the last element. `SetCount` with a larger count makes that slot visible again (`CL/Runtime/InteropServices/CollectionsMarshal.cs:140-159`): two live instances.
    - `Stack<T>.Pop` and `Queue<T>.Dequeue` leave the same kind of stale slot. Nothing on the list can reach it, which is why they keep `new`.
11. **`Span<T>.Clear` and `Array.Clear` are listed.** They overwrite elements with zero without disposing them. That drops values; it does not copy them.
12. **Views beyond `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ReadOnlyMemory<T>` and `ArraySegment<T>`:**
    - `Span<T>.Enumerator`, `ReadOnlySpan<T>.Enumerator`, `ArraySegment<T>.Enumerator`: these hold the span or array and an index.
    - `ImmutableArray<T>` and its `Enumerator`: `internal readonly T[]? array`, at `Lib/System.Collections.Immutable/src/System/Collections/Immutable/ImmutableArray_1.Minimal.cs:54`.
    - `ReadOnlySequence<T>`: two objects and two integers (`Lib/System.Memory/src/System/Buffers/ReadOnlySequence.cs:18-21`).
    - `TaskAwaiter<T>`, `ConfiguredTaskAwaitable<T>` and its awaiter: these hold only a `Task<T>` reference (`CL/Runtime/CompilerServices/TaskAwaiter.cs:292,467`).
    - `ValueTaskAwaiter<T>` and the `ConfiguredValueTaskAwaitable<T>` types are **not** views: they hold a `ValueTask<T>`, which stores the result inline.

## 4. Members by type

`L` = listed, `L new` = listed with `new`, `R` = rejected.

### 4.1 Awaiting

#### `Task<T>` (`CL/Threading/Tasks/Future.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `Result` | L | Returns `m_result`; the hand-out | Future.cs:428-465 |
| `GetAwaiter()` | L | Wraps `this` in a `TaskAwaiter<T>` (a view) | Future.cs:506 |
| `ConfigureAwait(bool)`, `ConfigureAwait(ConfigureAwaitOptions)` | L | Wraps `this` | Future.cs:517, 527 |
| `WaitAsync` ×5 | R | The promise copies the result: two live instances (section 3, item 1) | Future.cs:549-616, Task.cs:2925 |
| `ContinueWith` ×20 | R (not reviewed) | Out of the basic scope | Future.cs:631-1375 |
| constructors with `Func<T>` | R (not reviewed) | `Task.Run<T>` covers the need | Future.cs:107-271 |

#### `Task` statics (`CL/Threading/Tasks/Task.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `FromResult<T>(T)` | L | Moves the argument into a new task; cached default task (section 3, item 4) | Task.cs:5386 |
| `FromCanceled<T>`, `FromException<T>` | L | No `T` value | Task.cs:5479, 5455 |
| `Run<T>(Func<T>)`, `Run<T>(Func<T>, CancellationToken)` | L | `m_result = func()`, a move from checked code | Task.cs:5557, 5575; Future.cs:490 |
| `Run<T>(Func<Task<T>>)` ×2 | R | `UnwrapPromise<T>` does `TrySetResult(taskTResult.Result)` while the inner task, which the delegate may return from a field, keeps its result | Task.cs:5636-5668, 7552 |
| `WhenAll<T>` ×3 | R | Copies every result into a `T[]` (section 3, item 2) | Task.cs:6252, 6335, 6373 |
| `WhenAny<T>` ×4, `WhenEach<T>` ×3 | R | Hands back an argument task (section 3, item 3) | Task.cs:6811-6865, 6898-6913 |

#### Awaiters and awaitables (`CL/Runtime/CompilerServices/`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `TaskAwaiter<T>`: `IsCompleted`, `GetResult`, `OnCompleted`, `UnsafeOnCompleted` | L | `GetResult` returns `m_task.ResultOnSuccess`; the rest only look at the task | TaskAwaiter.cs:305-336 |
| `ConfiguredTaskAwaitable<T>.GetAwaiter` | L | Wraps the task | TaskAwaiter.cs:454 |
| `ConfiguredTaskAwaitable<T>.ConfiguredTaskAwaiter`: the same four members | L | As for `TaskAwaiter<T>` | TaskAwaiter.cs:485-516 |
| `ValueTaskAwaiter<T>`: the same four members | L | `GetResult` returns `_value.Result`; the others read `_obj` only | ValueTaskAwaiter.cs:118-179 |
| `ConfiguredValueTaskAwaitable<T>.GetAwaiter` | L | `new ConfiguredValueTaskAwaiter(in _value)`: a copy that is the hand-out | ConfiguredValueTaskAwaitable.cs:131 |
| `ConfiguredValueTaskAwaitable<T>.ConfiguredValueTaskAwaiter`: the same four members | L | As for `ValueTaskAwaiter<T>` | ConfiguredValueTaskAwaitable.cs:146-215 |

#### `ValueTask<T>` and `ValueTask` (`CL/Threading/Tasks/ValueTask.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `#ctor(T)`, `#ctor(Task<T>)`, `#ctor(IValueTaskSource<T>, short)` | L | Store the argument | ValueTask.cs:482-523 |
| `AsTask()` | L | Inline result: `Task.FromResult(_result)`, the hand-out; source: `GetResult` moves the result into the task; task: returns it | ValueTask.cs:573-680 |
| `Preserve()` | L | `this`, or a value task over `AsTask()` | ValueTask.cs:592 |
| `IsCompleted`, `IsCompletedSuccessfully`, `IsFaulted`, `IsCanceled` | L | Read `_obj` only | ValueTask.cs:702-795 |
| `Result` | L | The hand-out | ValueTask.cs:798-818 |
| `GetAwaiter()` | L | `new ValueTaskAwaiter<TResult>(in this)` | ValueTask.cs:823 |
| `ConfigureAwait(bool)` | L | Copies the fields into the awaitable; an example of the rules | ValueTask.cs:831 |
| `Equals(ValueTask<T>)`, `Equals(object)`, `==`, `!=` | R | `EqualityComparer<TResult>.Default.Equals(_result, …)` | ValueTask.cs:548-564 |
| `GetHashCode()` | R | `_result.GetHashCode()` on a read-only field: defensive copy | ValueTask.cs:542 |
| `ToString()` | R | `TResult result = Result;` then `ToString` | ValueTask.cs:835 |
| `ValueTask.FromResult<T>`, `FromCanceled<T>`, `FromException<T>` | L | Construct a value task | ValueTask.cs:119-144 |

#### `IValueTaskSource<T>`

| Member | Decision | Reason |
|---|---|---|
| `GetResult(short)` | L | Specified to hand out the result |
| `GetStatus(short)`, `OnCompleted(...)` | L | No `T` value |

#### `TaskCompletionSource<T>` (`CL/Threading/Tasks/TaskCompletionSource_T.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| 4 constructors, `Task` | L | Create or return the task | :37-76 |
| `SetResult`, `TrySetResult` | L | `_task.TrySetResult(result)` stores it (`Future.cs:371`) | :197, 216 |
| `SetException` ×2, `TrySetException` ×2, `SetCanceled` ×2, `TrySetCanceled` ×2 | L | No `T` value | :87-280 |
| `SetFromTask`, `TrySetFromTask` | R | `_task.TrySetResult(completedTask.Result)`: two tasks hold the value | :305-336 |

#### Async method builders (`CL/Runtime/CompilerServices/`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `AsyncValueTaskMethodBuilder<T>`: `Create`, `Start`, `SetStateMachine`, `SetException`, `AwaitOnCompleted`, `AwaitUnsafeOnCompleted` | L | No `T` value; awaiters are used through `ref` | AsyncValueTaskMethodBuilderT.cs:29-102 |
| `AsyncValueTaskMethodBuilder<T>`: `SetResult`, `Task` | L | Store, then hand out (section 3, item 7) | AsyncValueTaskMethodBuilderT.cs:45-70 |
| `AsyncTaskMethodBuilder<T>`: the same eight members | L | `SetResult` goes through `FromResult` or `TrySetResult`; awaiter used in place | AsyncTaskMethodBuilderT.cs:27-100, 428-480 |

### 4.2 Spans, memory and arrays

#### `Span<T>` (`CL/Span.cs`), `ReadOnlySpan<T>` (`CL/ReadOnlySpan.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| constructors from an array, a pointer and a reference | L | Store a reference and a length | Span.cs:41-127; ReadOnlySpan.cs:41-121 |
| `Item`, `Length`, `IsEmpty`, `Empty`, `GetPinnableReference` | L | `ref` or a count | Span.cs:146-221, 282 |
| `==`, `!=` | L | Compare reference and length | Span.cs:183, 364 |
| implicit conversions from `T[]` and `ArraySegment<T>`, and `Span<T>` to `ReadOnlySpan<T>` | L | Build a view | Span.cs:210-215, 371 |
| `GetEnumerator`, `Enumerator.MoveNext`, `Enumerator.Current` | L | `Current` is `ref _span[_index]` | Span.cs:224-264 |
| `Slice` ×2 | L | A view | Span.cs:395, 412 |
| `Clear` | L | Zeroes elements: drops, no copy | Span.cs:294-306 |
| `ToString` | L | For `T` other than `char`: type name and length | Span.cs:378-388 |
| `Fill`, `CopyTo`, `TryCopyTo`, `ToArray` | R | Duplicate | Span.cs:310-437 |
| `Equals(object)`, `GetHashCode` | R | Always throw; nothing to gain | ReadOnlySpan.cs:188-202 |
| `ReadOnlySpan<T>.CastUp<TDerived>` | R | `TDerived : class`; never a non-copyable struct | ReadOnlySpan.cs:225 |
| explicit `IEnumerator<T>.Current` on the enumerators | R | Returns by value; reachable only through the interface | Span.cs:266 |

#### `Memory<T>` (`CL/Memory.cs`), `ReadOnlyMemory<T>` (`CL/ReadOnlyMemory.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| constructors, implicit conversions, `Empty`, `Length`, `IsEmpty`, `Slice` ×2 | L | A view | Memory.cs:37-260 |
| `Span` | L | Builds a span over the same storage; `MemoryManager<T>.GetSpan` gets no `T` | Memory.cs:263-350 |
| `Pin` | L | Pins and returns a `MemoryHandle`; no element touched | Memory.cs:382-436 |
| `Equals(Memory<T>)`, `GetHashCode` | L | Identity of the object, index and length | Memory.cs:467-484 |
| `ToString` | L | Type name and length for `T` other than `char` | Memory.cs:208-215 |
| `CopyTo`, `TryCopyTo`, `ToArray` | R | Duplicate | Memory.cs:362-440 |
| `Equals(object)` | R | Not needed; it takes a boxed view | Memory.cs:447 |

#### `ArraySegment<T>` (`CL/ArraySegment.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| constructors, `Empty`, `Array`, `Offset`, `Count`, `Slice` ×2, implicit conversion from `T[]` | L | A view or its parts | :37-173 |
| `Item` | L | Getter hands out `_array[i]`; setter stores | :73-93 |
| `GetEnumerator`, `Enumerator.MoveNext`, `Current`, `Dispose` | L | `Current` hands out `_array[_current]` | :95, 281-333 |
| `Equals(ArraySegment<T>)`, `==`, `!=`, `GetHashCode` | L | Compare or hash the array reference, offset and count | :101-171 |
| `CopyTo` ×3, `ToArray` | R | Duplicate | :104-155 |
| explicit `IList<T>`/`ICollection<T>` members (`IndexOf`, `Contains`, …) | R | Compare | :197-250 |

#### `MemoryExtensions` (`CL/MemoryExtensions.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `AsSpan` ×10, `AsMemory` ×8 (arrays and `ArraySegment<T>`) | L | `new Span<T>(…)` / `new Memory<T>(…)` | :26-70, 3695-3901 |
| `Overlaps` ×4 | L | Compares addresses only | :4076-4108 |
| `Reverse<T>(Span<T>)` | L | Swaps through a temporary: moves | :3683; SpanHelpers.cs:331-345 |
| `Contains`, `IndexOf*`, `LastIndexOf*`, `SequenceEqual`, `SequenceCompareTo`, `StartsWith`, `EndsWith`, `Sort`, `BinarySearch`, `Count`, `Replace`, `Split`, `Trim*` | R | Compare, or call user code by value | |
| `CopyTo<T>(T[], …)`, `ToArray`-style members | R | Duplicate | |

#### `System.Array` (`CL/Array.cs`, `CLR/Array.CoreCLR.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `Length`, `LongLength`, `Rank` | L | Read the header | Array.CoreCLR.cs:288-295 |
| `GetLength`, `GetLongLength`, `GetLowerBound`, `GetUpperBound` | L | Read the header | Array.cs:764-1004 |
| `Empty<T>()` | L | A shared empty array | Array.cs:1456 |
| `Clear(Array)`, `Clear(Array, int, int)` | L | Zeroes: drops, no copy | Array.cs:659-700 |
| `Reverse<T>(T[])`, `Reverse<T>(T[], int, int)` | L | Swaps in place | Array.cs:2275-2297 |
| `Reverse(Array)`, `Reverse(Array, int, int)` | R | Other element types go through `GetValue`/`SetValue`: boxed copies | Array.cs:2210-2272 |
| `Resize<T>` | R | Copies into a new array; another reference to the old array keeps the originals | Array.cs:42-67 |
| `Copy`, `ConstrainedCopy`, `Clone`, `CopyTo`, `Fill`, `IndexOf`, `LastIndexOf`, `Find*`, `Exists`, `TrueForAll`, `ForEach`, `Sort`, `BinarySearch`, `ConvertAll`, `AsReadOnly`, `GetValue`, `SetValue`, `GetEnumerator` | R | Copy, compare, call user code or box | |
| `Initialize` | R (not reviewed) | Not needed | |

#### `GC`, `RuntimeHelpers`, `Nullable` helper

| Member | Decision | Reason | Source |
|---|---|---|---|
| `GC.AllocateArray<T>(int, bool)` | L new | A new zeroed array | CLR/GC.CoreCLR.cs:841 |
| `GC.AllocateUninitializedArray<T>(int, bool)` | L new | A new array; contents are unspecified for a reference-free `T` (section 3, item 5) | CLR/GC.CoreCLR.cs:798 |
| `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` | L | No value | CL/Runtime/CompilerServices/RuntimeHelpers.cs:175 |
| `RuntimeHelpers.GetSubArray<T>` | R | Copies a range | RuntimeHelpers.cs:26 |
| `RuntimeHelpers.CreateSpan<T>(RuntimeFieldHandle)` | R | Only for primitive RVA data; not applicable | |
| `Nullable.GetValueRefOrDefaultRef<T>` | L | `ref readonly` to the inline value | CL/Nullable.cs:130 |

#### `MemoryMarshal` (`CL/Runtime/InteropServices/MemoryMarshal.cs`, `CLR/Runtime/InteropServices/MemoryMarshal.CoreCLR.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `GetReference` ×2, `GetArrayDataReference<T>(T[])` | L | `ref` to the first element | :80-86; CoreCLR:22 |
| `CreateSpan`, `CreateReadOnlySpan` | L | A view over a reference | :226, 243 |
| `AsMemory<T>(ReadOnlyMemory<T>)` | L | A view | :73 |
| `TryGetArray`, `TryGetMemoryManager` ×2 | L | Hand out the underlying storage | :270-353 |
| `CreateFromPinnedArray` | L | A view | :601 |
| `GetArrayDataReference(Array)` | R | `ref byte`: the type is lost | CoreCLR:38 |
| `AsBytes` ×2, `Cast` ×2, `AsRef<T>(Span<byte>)` ×2 | R | Reinterpret as another type | :30-169, 549-571 |
| `Read`, `TryRead`, `Write`, `TryWrite` | R | Copy through bytes | :468-526 |
| `ToEnumerable` | R | Yields copies by value from an `IEnumerable<T>` that can be enumerated again; not needed | :379 |

#### `CollectionsMarshal` (`CL/Runtime/InteropServices/CollectionsMarshal.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `AsSpan<T>(List<T>)` | L | A span over `_items` | :23 |
| `GetValueRefOrNullRef` ×2, `GetValueRefOrAddDefault` ×2 | L | `ref` into the entry; see section 3, item 6 on `TKey` | :78-123 |
| `SetCount<T>` | R | Re-exposes stale slots (section 3, item 10) | :140-159 |

#### Other marshal classes

| Member | Decision | Reason | Source |
|---|---|---|---|
| `ImmutableCollectionsMarshal.AsImmutableArray`, `AsArray`, `AsMemory(Builder)` | L | Same storage, no copy | Lib/System.Collections.Immutable/src/System/Runtime.InteropServices/ImmutableCollectionsMarshal.cs:32-68 |
| `SequenceMarshal.TryGetReadOnlySequenceSegment`, `TryGetArray`, `TryGetReadOnlyMemory` | L | Hand out the underlying storage | Lib/System.Memory/src/System/Runtime/InteropServices/SequenceMarshal.cs:18-50 |
| `SequenceMarshal.TryRead<T>` | R | `T : unmanaged`; copies from bytes | :73 |
| `Marshal.SizeOf<T>()`, `Marshal.OffsetOf<T>(string)` | L | No value | CL/Runtime/InteropServices/Marshal.cs:122, 182 |
| `Marshal.SizeOf<T>(T)`, `StructureToPtr<T>`, `PtrToStructure<T>` ×2, `GetNativeVariantForObject<T>`, `GetObjectForNativeVariant<T>` | R | Copy the value | Marshal.cs:97, 545, 594, 604 |
| `Marshal.UnsafeAddrOfPinnedArrayElement<T>` | R | Hands out a pointer: the type is lost | Marshal.cs:173 |
| `Marshal.DestroyStructure<T>` | R | Not applicable | Marshal.cs:627 |
| `NativeMemory` | none | Has no generic members | |

#### `Unsafe` (`CL/Runtime/CompilerServices/Unsafe.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `SizeOf` | L | No value | :48 |
| `Add` ×4, `AddByteOffset` ×2, `Subtract` ×4, `SubtractByteOffset` ×2 | L | Reference or pointer arithmetic | :100-210, 629, 784-907 |
| `AreSame`, `IsAddressGreaterThan`, `IsAddressLessThan`, `ByteOffset` | L | Compare addresses | :235, 396, 420, 708 |
| `AsRef<T>(ref readonly T)`, `AsRef<T>(void*)` | L | A typed reference, no copy (section 3, item 9) | :674, 690 |
| `NullRef`, `IsNullRef` | L | No value | :727, 748 |
| `AsPointer` | R | Hands out a pointer: the type is lost | :29 |
| `As<TFrom, TTo>(ref)`, `As<T>(object)` | R | Reinterpret | :64, 81 |
| `BitCast`, `Copy` ×2, `Read`, `Write`, `ReadUnaligned` ×2, `WriteUnaligned` ×2 | R | Copy | :254-661 |
| `SkipInit` | R | Hands out an uninitialized value that can hold stale stack bits | :769 |
| `Unbox` | R | `ref` into a box; boxing already gets a warning, and nothing needs it | :930 |

#### `ArrayPool<T>`, `MemoryPool<T>` (`CL/Buffers/ArrayPool.cs`, `SharedArrayPool.cs`; `Lib/System.Memory/src/System/Buffers/MemoryPool.cs`)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `ArrayPool<T>.Shared`, `Create` ×2 | L | Pool objects | ArrayPool.cs:39-60 |
| `ArrayPool<T>.Rent` | L new | Hands out an array taken from the pool or newly allocated (section 3, item 5) | ArrayPool.cs:77; SharedArrayPool.cs:71-110 |
| `ArrayPool<T>.Return(T[], bool)` | L | Stores the array; `Array.Clear` if asked | ArrayPool.cs:99; SharedArrayPool.cs:122-147 |
| `MemoryPool<T>.Shared`, `MaxBufferSize`, `Dispose` | L | No value | MemoryPool.cs:18-39 |
| `MemoryPool<T>.Rent` | L new | Wraps a rented array in an owner | MemoryPool.cs:24 |
| `IMemoryOwner<T>.Memory` | L | A view of the owned buffer | CL/Buffers/IMemoryOwner.cs |

### 4.3 Atomic helpers

| Member | Decision | Reason | Source |
|---|---|---|---|
| `Interlocked.Exchange<T>(ref T, T)` | L new | .NET 9+: no constraint, but any struct that is not primitive or enum throws `NotSupportedException`. .NET 8: `where T : class`. So it only ever applies to containers, and it takes the reference out of the location | CL/Threading/Interlocked.cs:243-283 |
| `Interlocked.CompareExchange<T>(ref T, T, T)` | L | Same constraint situation; reference comparison, no user code; the returned value may still be in the location, hence no `new` | Interlocked.cs:495 |
| `Volatile.Read<T>`, `Volatile.Write<T>` | L | `where T : class?` in every version: containers only | CL/Threading/Volatile.cs:221, 226 |

### 4.4 Collections (basic level only)

| Type | Listed | Rejected (main reasons) | Source |
|---|---|---|---|
| `List<T>` | `#ctor`, `#ctor(int)`, `Add`, `Insert`, `RemoveAt`, `Clear`, `Count`, `Capacity` (setter moves to a new private array), `Item`, `EnsureCapacity`, `TrimExcess`, `GetEnumerator`, `Enumerator.MoveNext`/`Current`/`Dispose` (copy into `_current`; section 2) | `#ctor(IEnumerable<T>)`, `AddRange`, `InsertRange`, `GetRange`, `Slice`, `CopyTo` ×3, `ToArray`, `ConvertAll` (duplicate); `Contains`, `IndexOf`, `LastIndexOf`, `Remove(T)`, `BinarySearch`, `Sort` ×4 (compare); `Find*`, `Exists`, `ForEach`, `TrueForAll`, `RemoveAll` (delegate by value); `AsReadOnly`, `Reverse`, `RemoveRange` (not reviewed) | CL/Collections/Generic/List.cs:37-1229 |
| `Stack<T>` | `#ctor` ×2, `Push`, `Pop` new, `TryPop` new, `Peek`, `TryPeek`, `Count`, `Clear`, enumerator ×3 | `#ctor(IEnumerable<T>)`, `CopyTo`, `ToArray` (duplicate); `Contains` (compare) | Lib/System.Collections/src/System/Collections/Generic/Stack.cs:35-391; `Pop` at :221-243 |
| `Queue<T>` | `#ctor` ×2, `Enqueue`, `Dequeue` new, `TryDequeue` new, `Peek`, `TryPeek`, `Count`, `Clear`, enumerator ×3 | `#ctor(IEnumerable<T>)`, `CopyTo`, `ToArray` (duplicate); `Contains` (compare) | CL/Collections/Generic/Queue.cs:32-482; `Dequeue` at :194-237 |
| `Dictionary<TKey, TValue>` | `#ctor` ×2, `Add`, `TryAdd`, `Remove(TKey)`, `Remove(TKey, out TValue)` new (moves the value out and clears the slot, :1405-1417), `TryGetValue`, `Item`, `ContainsKey`, `Count`, `Clear`, enumerator ×3 | `ContainsValue` (compare); constructors from a collection, `CopyTo`, `Values`/`Keys` (not reviewed); explicit `ICollection<KeyValuePair>` members (compare) | CL/Collections/Generic/Dictionary.cs:40-1811 |
| `LinkedList<T>`, `ConcurrentQueue<T>`, `ConcurrentStack<T>`, `ConcurrentDictionary<TKey, TValue>` | none | Dropped on purpose | |

### 4.5 Interfaces (judged by specification; basic level only)

| Member | Decision | Reason |
|---|---|---|
| `IEnumerable<T>.GetEnumerator`, `IEnumerator<T>.Current`, `IEnumerator.MoveNext`, `IDisposable.Dispose` | L | `foreach` over a typed sequence; `MoveNext` and `Dispose` hand out nothing |
| `ICollection<T>.Count`, `IsReadOnly`, `Add`, `Clear` | L | Store or count |
| `ICollection<T>.Contains`, `Remove`, `CopyTo` | R | Specified to compare or copy |
| `IList<T>.Item`, `Insert`, `RemoveAt` | L | Hand out or store |
| `IList<T>.IndexOf` | R | Compares |
| `IReadOnlyCollection<T>.Count`, `IReadOnlyList<T>.Item` | L | |
| `IDictionary<TKey, TValue>.Item`, `Add`, `Remove(TKey)`, `TryGetValue`, `ContainsKey`; `IReadOnlyDictionary<TKey, TValue>.Item`, `TryGetValue`, `ContainsKey` | L | Judged for `TValue` (section 3, item 6) |
| `IDictionary.Keys`/`Values` | R (not reviewed) | |
| `IAsyncEnumerable<T>.GetAsyncEnumerator`, `IAsyncEnumerator<T>.Current`, `MoveNextAsync`, `IAsyncDisposable.DisposeAsync` | L | `await foreach` |

### 4.6 Generic structs (none is a view)

| Member | Decision | Reason | Source |
|---|---|---|---|
| `Nullable<T>`: `#ctor(T)`, `HasValue`, `Value`, `GetValueOrDefault()`, `GetValueOrDefault(T)`, implicit conversion from `T`, explicit conversion to `T` | L | Store or hand out | CL/Nullable.cs:27-74 |
| `Nullable<T>`: `Equals`, `GetHashCode`, `ToString` | R (not needed) | Call user code on the value (in place) | :58-67 |
| `Nullable.Compare<T>`, `Nullable.Equals<T>` | R | Comparer by value | :79-94 |
| `KeyValuePair<TKey, TValue>`: `#ctor`, `Key`, `Value`, `Deconstruct`; `KeyValuePair.Create` | L | Store or hand out | CL/Collections/Generic/KeyValuePair.cs:15-51 |
| `KeyValuePair<TKey, TValue>.ToString` | R | Passes both by value | :45 |
| `ValueTuple<…>` constructors (arities 1 to 8), `ValueTuple.Create` (arities 1 to 8) | L | `Item1 = item1; …` | CL/ValueTuple.cs:445 and the others |
| `ValueTuple` `Equals`, `GetHashCode`, `CompareTo`, `ToString` | R | Comparers by value | |

### 4.7 Opaque and untyped sinks

| Member | Decision | Reason | Source |
|---|---|---|---|
| `Object.GetType()` | L | Header only | CLR/Object.CoreCLR.cs:14 |
| `Object.ReferenceEquals` | L | Reference comparison | CL/Object.cs:67 |
| `Object.Equals(object)`, `Equals(object, object)`, `GetHashCode()`, `ToString()` | R | Virtual: a container such as `Tuple<Guard, int>` compares, hashes or formats its items by value | CL/Object.cs:48-74 |
| `ArgumentNullException.ThrowIfNull(object, string)` | L | Null check | CL/ArgumentNullException.cs:55 |
| `ObjectDisposedException.ThrowIf(bool, object)` | L | Uses `instance?.GetType().FullName` | CL/ObjectDisposedException.cs:57; CL/ThrowHelper.cs:456-458 |
| `GC.KeepAlive` | L | Nothing | CLR/GC.CoreCLR.cs:288 |
| `RuntimeHelpers.GetHashCode(object)` | L | Identity hash | CLR/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:244 |
| `Monitor.Enter` ×2, `TryEnter` ×6, `Exit`, `IsEntered`, `Wait` ×3, `Pulse`, `PulseAll` | L | Use the object's sync block only | CLR/Threading/Monitor.CoreCLR.cs:33, 154; CL/Threading/Monitor.cs:24 |
| `Task`: `IsCompleted`, `IsCompletedSuccessfully`, `IsFaulted`, `IsCanceled`, `Status`, `Exception`, `Id`, `AsyncState`, `CreationOptions`, `Dispose` | L | State flags and contingent properties; no result | CL/Threading/Tasks/Task.cs:1190-1579 |
| `Task.Wait` ×6 | L | Waits; no result | Task.cs:2623-2754 |
| `Task.WaitAsync` ×5 (non-generic) | L | `CancellationPromise<VoidTaskResult>`; no result copy | Task.cs:2795-2849 |
| `Task.GetAwaiter`, `Task.ConfigureAwait` ×2 | L | Awaiting a `Task<T>` as a `Task` | |
| `Task.WhenAll(Task[])`, `(IEnumerable<Task>)`, `(ReadOnlySpan<Task>)` | L | No result is read | Task.cs:5929-6250 |
| `Task.WhenAny(Task[])`, `(Task, Task)`, `(IEnumerable<Task>)`, `(ReadOnlySpan<Task>)` | L | Returns `Task<Task>`; reaching the result needs a cast | Task.cs:6525-6717 |
| `Task.WaitAll` ×7, `Task.WaitAny` ×5 | L | Wait only | Task.cs:4703-5314 |
| `Task.WhenEach(Task…)` ×3 | R (not reviewed) | Not needed | Task.cs:6879-6892 |

### 4.8 `view` and `noncopyable` types

See section 3, items 8 and 12, for the reasoning and sources. Checked and not marked:

- **Left copyable:** `ValueTask<T>`, `Nullable<T>`, `KeyValuePair`, `ValueTuple` and `List<T>.Enumerator`. These are generic structs that hold `T` inline, so they are already non-copyable over a non-copyable `T` (see [Non-copyable Types](../../Docs/articles/rules/non-copyable-types.md)), and they are not views.
- **Not marked `noncopyable`:**
  - `CancellationTokenRegistration`: copies dispose idempotently, by id.
  - `SpinWait`: a copy only loses the count.
  - `SequenceReader<T>` and `Utf8JsonReader`: copying them is the documented way to save state.
  - The method builder structs: compiler-owned.

## 5. ID validation

- **Program:** a small validator, which is not kept in the repository, built on Microsoft.CodeAnalysis.CSharp 5.0.0. It loads every `*.dll` of a reference pack into a `CSharpCompilation` and calls `DocumentationCommentId.GetSymbolsForDeclarationId` for each entry. An entry passes if it resolves to exactly one public symbol, its marker is valid, and a `view` or `noncopyable` marker sits on a struct. It also checks for duplicates (none).
- **Results:**

| Reference pack | Resolve to exactly one public symbol |
|---|---|
| Microsoft.NETCore.App.Ref 11.0.0 (newest installed) | 457 of 457 |
| 10.0.14 | 457 of 457 |
| 9.0.19 | 453 of 457 |
| 8.0.33 | 446 of 457 |

- **Entries that do not resolve on .NET 8** (they stay on the list, marked with a trailing comment):
  - .NET 10 and later: `GCHandle<T>`, `PinnedGCHandle<T>`, `WeakGCHandle<T>`, `ImmutableCollectionsMarshal.AsMemory(ImmutableArray<T>.Builder)`.
  - .NET 9 and later: `Lock.Scope`, the two `CollectionsMarshal` overloads taking `Dictionary<,>.AlternateLookup<>`, `Task.WhenAll(ReadOnlySpan<Task>)`, `Task.WhenAny(ReadOnlySpan<Task>)`, `Task.WaitAll(ReadOnlySpan<Task>)`, `Task.WaitAll(IEnumerable<Task>, CancellationToken)`.
- **Parser note:** `#` may only start a comment at the start of a line or after white space. A plain "`#` starts a comment" rule cuts every `#ctor` entry. The first run of the validator did exactly that.

## 6. Questions that the draft raised

The draft raised these questions about how the rules decide an entry. Each was settled later:

1. **One entry covers every type parameter of a member.** A member that is safe for `TValue` but compares `TKey`, as every
   `Dictionary` lookup does, could only be listed or rejected as a whole. Settled with `for=`, which limits an entry to some type
   parameters (section 7).
2. **"Leaves two live instances" against copies that are handed out.** Enumerators, `ValueTask<T>.ConfigureAwait` and `AsTask`
   all keep a second instance internally, and `Task<T>.WaitAsync` has the same shape. Settled with the rule that a copy inside
   what is handed out is allowed when every way to get at it is a by-value read through a listed member, which the analyzer
   judges by where the container came from. `WaitAsync` is listed (section 7).
3. **How new is a container built from stored containers?** `Task.WhenAny<T>(t1, t2)` with stored `t1` and `t2` takes a `params`
   array created at the call. Settled with the rule that a created array or collection is as new as what is put into it, so the
   result follows the tasks that are passed (section 7).
4. **Whether compiler-generated builder calls count as uses.** Settled: the analyzer does not examine the calls that the compiler
   generates in an `async` method, and the builder entries were removed (section 7).
5. **The non-generic members that a typed `foreach` needs.** `IEnumerator.MoveNext` and `IDisposable.Dispose` are reached on an
   `IEnumerator<Guard>` through a non-generic interface, which would be a conversion to a type that hides the non-copyable type.
   So they are listed.
6. **`Interlocked` and `Volatile` entries can only apply to containers** (section 4.3). `Interlocked.Exchange` throws for a
   struct that is not a primitive or an enum, so its `new` marker serves a container, out of whose location it takes the
   reference.

## 7. Changes made after the draft (2026-11-16)

These were made to the list after a review of the points in sections 3 and 6. The tables in section 4 still show the draft's verdicts.

- **`Task<T>.WaitAsync` (5 overloads) is now listed.** It puts a copy of the result in the task it returns, but the only way to get at that copy is a by-value read through a listed member, which the analyzer judges by where the task came from. This is the same shape as `ValueTask<T>.AsTask`. The generic `Task.WhenAll<TResult>` and `WhenAny<TResult>` stay rejected: they hand out the copies in an array, whose elements can be used in place.
- **The async method builder entries (16) are removed.** The analyzer does not examine the calls that the compiler generates in an `async` method, so the entries had no use, and the builders keep a private copy of the result.
- **Dictionary entries are limited with `;for=TValue` (27 entries):** `Dictionary<TKey, TValue>` and its enumerator, `IDictionary<TKey, TValue>`, `IReadOnlyDictionary<TKey, TValue>`, and the `CollectionsMarshal.GetValueRef...` methods. A non-copyable `TKey` then gets a warning, which closes the hole described in section 3.
- **`ValueTask<T>.#ctor(IValueTaskSource<T>, short)` is marked `new`.** With no marker, the task counted as stored whenever the source was, which gave a warning on every implementation of a value-task source. Two tasks from one source and token would each count as new; the contract of `IValueTaskSource<T>` forbids that.
- **`Task<T>` is marked `erasable`,** a marker that was added to the list format later: its base type `Task` and its interfaces `IAsyncResult` and `IDisposable` expose nothing of `TResult`, so a conversion to them is a dead end.
- **`ManualResetValueTaskSourceCore<T>` gets its eight members.** `GetResult` has no marker on purpose: it returns the result and leaves it in the core until `Reset`, so a by-value read from a stored core gets a warning. `SetResult` stores the value it is given, which is a move in.
- **`Unsafe.Unbox<T>` is listed.** It returns a reference into the box.
- **Not read from the pinned runtime commit:** the last two points are entered from the documented behaviour of those members, not from a new reading of the source.
- **The second review confirmed the last two points against the runtime source:** the eight members of `ManualResetValueTaskSourceCore<T>`, `Unsafe.Unbox<T>`, and `Task<T>` as erasable.
- **Sixteen `borrows` entries,** a marker that was added to the list format later, for members of the built-in non-copyable structs that take the struct by value. Read on dotnet/runtime `main`, 16 November 2026: `ToIntPtr` returns `value._handle` and `Equals` compares `_handle` in `GCHandle`, `GCHandle<T>`, `PinnedGCHandle<T>` and `WeakGCHandle<T>`; the `==`, `!=` and `IntPtr` conversion of `GCHandle` do the same; `GCHandleExtensions.GetAddressOfArrayData` and `GetAddressOfStringData` read `handle.Target` once; `AsyncFlowControl.Equals` compares `_thread`, and its operators call it. None frees, stores or changes the value.
- **Not given `borrows`:** `MemoryHandle..ctor(void*, GCHandle, IPinnable)` keeps the handle and frees it in `Dispose`, so a stored handle passed to it is a second owner. `JavaMarshal.GetContext(GCHandle)` was not read.
- **The generic `Task.WhenAny<TResult>` (4 overloads) is now listed,** on a later review's suggestion. It was rejected in the draft (section 3, item 3) because it hands back an argument task, and a `params` array created at the call counted as new then. Since then a created array or collection is as new as what is put into it, so the result follows the tasks that are passed: stored tasks give a stored result. It copies no result. `WhenAll<TResult>` and `WhenEach<TResult>` stay rejected.
- **`TaskCompletionSource<T>.Task` is marked `owned`, and `TaskCompletionSource<T>` is marked `erasable`.** Read in `TaskCompletionSource_T.cs` and `Future.cs` at the pinned commit: the task is a private readonly field that only the four constructors assign, `Task` is the only member that hands it out, and the class has no base class, no interface and no override of a member of `object`. `SetFromTask` and `TrySetFromTask` copy the result of another task into the source and must stay off the list while `Task` is `owned`. A `Task<TResult>` gives its result by value only. The list then has 477 entries.
- **The list now has 476 entries** (the count below is from before this addition).
- **The list now has 472 entries.** All resolve on .NET 10 and 11; 460 on .NET 9 and 453 on .NET 8. Nineteen entries are newer than .NET 8.

## 8. Members that make read-only storage writable: considered for removal, and kept (2026-11-17)

Five listed members turn something read-only into something writable: `Unsafe.AsRef<T>(in T)`, `MemoryMarshal.GetReference<T>(ReadOnlySpan<T>)`, `MemoryMarshal.AsMemory<T>(ReadOnlyMemory<T>)`, `MemoryMarshal.TryGetArray<T>(ReadOnlyMemory<T>, out ArraySegment<T>)` and `ImmutableCollectionsMarshal.AsArray<T>(ImmutableArray<T>)`. Several rules of the analyzer take `readonly` at its word, and a write through one of these breaks them: a test program with a guard whose `readonly` `Dispose` writes through `Unsafe.AsRef(in this)`, called as `maybe?.Dispose()`, builds with no diagnostic and releases one acquisition twice. They were taken off the draft for a while on that ground.

They were kept, on this principle: **the list says of a member only that it makes no copy, and the analyzer guarantees nothing about code that uses the unsafe classes to do what the language otherwise rules out.** `Unsafe.AsRef(in)` can change what must not change, and `Unsafe.NullRef<T>()` can hand a null reference to code that does not expect one; neither is the analyzer's to guard against. The criterion of section 2 stands as it is, with no second criterion. [What Is Not Covered](../../Docs/articles/rules/not-covered.md#unsafe-classes) says so.

The list has 477 entries.

## 9. Who keeps a container: the members with a parameter over an unmarked type parameter

A parameter whose type mentions a type parameter that is not marked is taken to keep the container that it is given (see [What the Keeping Check Follows](../../Docs/articles/rules/what-keeping-follows.md#code-that-nothing-checks)), unless the member's entry is marked `borrows`. Every member of the list with such a parameter, 92 of them, was read in the runtime source at `d757c03be2c3` for this one question: does it keep what it is given, only use it during the call, or hand it back. 16 carry `borrows`; none needed a second marker.

Runtime source read: dotnet/runtime `d757c03be2c39968bca1db2ce9349fce8f1463a3` (the commit that the list names).

| Member | Parameter | Verdict | Why |
|---|---|---|---|
| `M:System.Runtime.InteropServices.GCHandle`1.ToIntPtr(System.Runtime.InteropServices.GCHandle{`0})` | `GCHandle<T>` | only uses: `borrows` already | returns `value._handle` |
| `M:System.Runtime.InteropServices.GCHandle`1.Equals(System.Runtime.InteropServices.GCHandle{`0})` | `GCHandle<T>` | only uses: `borrows` already | compares `_handle` |
| `M:System.Runtime.InteropServices.PinnedGCHandle`1.ToIntPtr(System.Runtime.InteropServices.PinnedGCHandle{`0})` | `PinnedGCHandle<T>` | only uses: `borrows` already | returns `value._handle` |
| `M:System.Runtime.InteropServices.PinnedGCHandle`1.Equals(System.Runtime.InteropServices.PinnedGCHandle{`0})` | `PinnedGCHandle<T>` | only uses: `borrows` already | compares `_handle` |
| `M:System.Runtime.InteropServices.WeakGCHandle`1.ToIntPtr(System.Runtime.InteropServices.WeakGCHandle{`0})` | `WeakGCHandle<T>` | only uses: `borrows` already | returns `value._handle` |
| `M:System.Runtime.InteropServices.WeakGCHandle`1.Equals(System.Runtime.InteropServices.WeakGCHandle{`0})` | `WeakGCHandle<T>` | only uses: `borrows` already | compares `_handle` |
| `M:System.Runtime.InteropServices.GCHandleExtensions.GetAddressOfArrayData``1(System.Runtime.InteropServices.PinnedGCHandle{``0[]})` | `PinnedGCHandle<T[]>` | only uses: `borrows` already | reads `handle.Target` and returns a pointer to its data |
| `M:System.Threading.Tasks.Task.Run``1(System.Func{``0})` | `Func<T>` | keeps: no marker | `Task<TResult>.StartNew(null, function, ...)`: the delegate stays in the task until it has run |
| `M:System.Threading.Tasks.Task.Run``1(System.Func{``0},System.Threading.CancellationToken)` | `Func<T>` | keeps: no marker | `Task<TResult>.StartNew(null, function, ...)`: the delegate stays in the task until it has run |
| `M:System.Threading.Tasks.ValueTask`1.#ctor(System.Threading.Tasks.Task{`0})` | `Task<T>` | keeps: no marker | constructor: `_obj = task` |
| `M:System.Threading.Tasks.ValueTask`1.#ctor(System.Threading.Tasks.Sources.IValueTaskSource{`0},System.Int16)` | `IValueTaskSource<T>` | keeps: no marker | constructor: `_obj = source` |
| `M:System.Span`1.#ctor(`0[])` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept by its kind today (see note) |
| `M:System.Span`1.#ctor(`0[],System.Int32,System.Int32)` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept by its kind today (see note) |
| `M:System.Span`1.op_Equality(System.Span{`0},System.Span{`0})` | `Span<T> ; Span<T>` | only uses: `borrows` | compares `_length` and `_reference` |
| `M:System.Span`1.op_Inequality(System.Span{`0},System.Span{`0})` | `Span<T> ; Span<T>` | only uses: `borrows` | compares `_length` and `_reference` |
| `M:System.Span`1.op_Implicit(`0[])~System.Span{`0}` | `T[]` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.Span`1.op_Implicit(System.ArraySegment{`0})~System.Span{`0}` | `ArraySegment<T>` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.Span`1.op_Implicit(System.Span{`0})~System.ReadOnlySpan{`0}` | `Span<T>` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.ReadOnlySpan`1.#ctor(`0[])` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept by its kind today (see note) |
| `M:System.ReadOnlySpan`1.#ctor(`0[],System.Int32,System.Int32)` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept by its kind today (see note) |
| `M:System.ReadOnlySpan`1.op_Equality(System.ReadOnlySpan{`0},System.ReadOnlySpan{`0})` | `ReadOnlySpan<T> ; ReadOnlySpan<T>` | only uses: `borrows` | compares `_length` and `_reference` |
| `M:System.ReadOnlySpan`1.op_Inequality(System.ReadOnlySpan{`0},System.ReadOnlySpan{`0})` | `ReadOnlySpan<T> ; ReadOnlySpan<T>` | only uses: `borrows` | compares `_length` and `_reference` |
| `M:System.ReadOnlySpan`1.op_Implicit(`0[])~System.ReadOnlySpan{`0}` | `T[]` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.ReadOnlySpan`1.op_Implicit(System.ArraySegment{`0})~System.ReadOnlySpan{`0}` | `ArraySegment<T>` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.Memory`1.#ctor(`0[])` | `T[]` | keeps: no marker | constructor: `_object = array` |
| `M:System.Memory`1.#ctor(`0[],System.Int32,System.Int32)` | `T[]` | keeps: no marker | constructor: `_object = array` |
| `M:System.Memory`1.op_Implicit(`0[])~System.Memory{`0}` | `T[]` | hands back, followed: no marker | makes a view over the same array: the container under another type |
| `M:System.Memory`1.op_Implicit(System.ArraySegment{`0})~System.Memory{`0}` | `ArraySegment<T>` | hands back, followed: no marker | makes a view over the same array: the container under another type |
| `M:System.Memory`1.op_Implicit(System.Memory{`0})~System.ReadOnlyMemory{`0}` | `Memory<T>` | hands back, followed: no marker | makes a view over the same array: the container under another type |
| `M:System.Memory`1.Equals(System.Memory{`0})` | `Memory<T>` | only uses: `borrows` | compares `_object`, `_index` and `_length` |
| `M:System.ReadOnlyMemory`1.#ctor(`0[])` | `T[]` | keeps: no marker | constructor: `_object = array` |
| `M:System.ReadOnlyMemory`1.#ctor(`0[],System.Int32,System.Int32)` | `T[]` | keeps: no marker | constructor: `_object = array` |
| `M:System.ReadOnlyMemory`1.op_Implicit(`0[])~System.ReadOnlyMemory{`0}` | `T[]` | hands back, followed: no marker | makes a view over the same array: the container under another type |
| `M:System.ReadOnlyMemory`1.op_Implicit(System.ArraySegment{`0})~System.ReadOnlyMemory{`0}` | `ArraySegment<T>` | hands back, followed: no marker | makes a view over the same array: the container under another type |
| `M:System.ReadOnlyMemory`1.Equals(System.ReadOnlyMemory{`0})` | `ReadOnlyMemory<T>` | only uses: `borrows` | compares `_object`, `_index` and `_length` |
| `M:System.ArraySegment`1.#ctor(`0[])` | `T[]` | keeps: no marker | constructor: `_array = array` |
| `M:System.ArraySegment`1.#ctor(`0[],System.Int32,System.Int32)` | `T[]` | keeps: no marker | constructor: `_array = array` |
| `M:System.ArraySegment`1.Equals(System.ArraySegment{`0})` | `ArraySegment<T>` | only uses: `borrows` | compares `_array`, `_offset` and `_count` |
| `M:System.ArraySegment`1.op_Equality(System.ArraySegment{`0},System.ArraySegment{`0})` | `ArraySegment<T> ; ArraySegment<T>` | only uses: `borrows` | `a.Equals(b)` |
| `M:System.ArraySegment`1.op_Inequality(System.ArraySegment{`0},System.ArraySegment{`0})` | `ArraySegment<T> ; ArraySegment<T>` | only uses: `borrows` | `a.Equals(b)` |
| `M:System.ArraySegment`1.op_Implicit(`0[])~System.ArraySegment{`0}` | `T[]` | hands back, followed: no marker | makes a view over the same array: the container under another type |
| `M:System.MemoryExtensions.AsSpan``1(``0[])` | `T[]` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(``0[],System.Int32)` | `T[]` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(``0[],System.Index)` | `T[]` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(``0[],System.Range)` | `T[]` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(``0[],System.Int32,System.Int32)` | `T[]` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(System.ArraySegment{``0})` | `ArraySegment<T>` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(System.ArraySegment{``0},System.Int32)` | `ArraySegment<T>` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(System.ArraySegment{``0},System.Index)` | `ArraySegment<T>` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(System.ArraySegment{``0},System.Int32,System.Int32)` | `ArraySegment<T>` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsSpan``1(System.ArraySegment{``0},System.Range)` | `ArraySegment<T>` | hands back, followed: no marker | returns a span over the array or segment: a ref struct |
| `M:System.MemoryExtensions.AsMemory``1(``0[])` | `T[]` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.AsMemory``1(``0[],System.Int32)` | `T[]` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.AsMemory``1(``0[],System.Index)` | `T[]` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.AsMemory``1(``0[],System.Int32,System.Int32)` | `T[]` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.AsMemory``1(``0[],System.Range)` | `T[]` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.AsMemory``1(System.ArraySegment{``0})` | `ArraySegment<T>` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.AsMemory``1(System.ArraySegment{``0},System.Int32)` | `ArraySegment<T>` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.AsMemory``1(System.ArraySegment{``0},System.Int32,System.Int32)` | `ArraySegment<T>` | hands back, followed: no marker | returns a `Memory<T>` over the array or segment |
| `M:System.MemoryExtensions.Overlaps``1(System.Span{``0},System.ReadOnlySpan{``0})` | `Span<T> ; ReadOnlySpan<T>` | only uses: `borrows` | compares the addresses of the two spans |
| `M:System.MemoryExtensions.Overlaps``1(System.Span{``0},System.ReadOnlySpan{``0},System.Int32@)` | `Span<T> ; ReadOnlySpan<T>` | only uses: `borrows` | compares the addresses of the two spans |
| `M:System.MemoryExtensions.Overlaps``1(System.ReadOnlySpan{``0},System.ReadOnlySpan{``0})` | `ReadOnlySpan<T> ; ReadOnlySpan<T>` | only uses: `borrows` | compares the addresses of the two spans |
| `M:System.MemoryExtensions.Overlaps``1(System.ReadOnlySpan{``0},System.ReadOnlySpan{``0},System.Int32@)` | `ReadOnlySpan<T> ; ReadOnlySpan<T>` | only uses: `borrows` | compares the addresses of the two spans |
| `M:System.MemoryExtensions.Reverse``1(System.Span{``0})` | `Span<T>` | only uses: `borrows` | `SpanHelpers.Reverse` in place |
| `M:System.Array.Reverse``1(``0[])` | `T[]` | only uses: `borrows` | `SpanHelpers.Reverse` on the array data, in place |
| `M:System.Array.Reverse``1(``0[],System.Int32,System.Int32)` | `T[]` | only uses: `borrows` | `SpanHelpers.Reverse` on the array data, in place |
| `M:System.Runtime.InteropServices.MemoryMarshal.GetReference``1(System.Span{``0})` | `Span<T>` | hands back, followed: no marker | returns a reference to the first element |
| `M:System.Runtime.InteropServices.MemoryMarshal.GetReference``1(System.ReadOnlySpan{``0})` | `ReadOnlySpan<T>` | hands back, followed: no marker | returns a reference to the first element |
| `M:System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference``1(``0[])` | `T[]` | hands back, followed: no marker | returns a reference to the first element |
| `M:System.Runtime.InteropServices.MemoryMarshal.AsMemory``1(System.ReadOnlyMemory{``0})` | `ReadOnlyMemory<T>` | hands back, followed: no marker | `new Memory<T>(memory._object, ...)`: the same object under another type |
| `M:System.Runtime.InteropServices.MemoryMarshal.TryGetArray``1(System.ReadOnlyMemory{``0},System.ArraySegment{``0}@)` | `ReadOnlyMemory<T> ; ArraySegment<T> (by reference)` | hands back, followed: no marker | hands the array back through `out ArraySegment<T>`; stores nothing |
| `M:System.Runtime.InteropServices.MemoryMarshal.TryGetMemoryManager``2(System.ReadOnlyMemory{``0},``1@)` | `ReadOnlyMemory<T>` | hands back, followed: no marker | hands the object behind the memory back through `out TManager`; stores nothing |
| `M:System.Runtime.InteropServices.MemoryMarshal.TryGetMemoryManager``2(System.ReadOnlyMemory{``0},``1@,System.Int32@,System.Int32@)` | `ReadOnlyMemory<T>` | hands back, followed: no marker | hands the object behind the memory back through `out TManager`; stores nothing |
| `M:System.Runtime.InteropServices.MemoryMarshal.CreateFromPinnedArray``1(``0[],System.Int32,System.Int32)` | `T[]` | hands back, followed: no marker | returns a `Memory<T>` over the array |
| `M:System.Runtime.InteropServices.CollectionsMarshal.AsSpan``1(System.Collections.Generic.List{``0})` | `List<T>` | hands back, followed: no marker | returns a span over the list's array: a ref struct |
| `M:System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef``2(System.Collections.Generic.Dictionary{``0,``1},``0)` | `Dictionary<T,T1>` | hands back, followed: no marker | returns a reference into the dictionary; stores nothing |
| `M:System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef``3(System.Collections.Generic.Dictionary{``0,``1}.AlternateLookup{``2},``2)` | `Dictionary<T,T1>.AlternateLookup<T2>` | hands back, followed: no marker | returns a reference into the dictionary; stores nothing |
| `M:System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault``2(System.Collections.Generic.Dictionary{``0,``1},``0,System.Boolean@)` | `Dictionary<T,T1>` | hands back, followed: no marker | returns a reference into the dictionary; stores nothing |
| `M:System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault``3(System.Collections.Generic.Dictionary{``0,``1}.AlternateLookup{``2},``2,System.Boolean@)` | `Dictionary<T,T1>.AlternateLookup<T2>` | hands back, followed: no marker | returns a reference into the dictionary; stores nothing |
| `M:System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray``1(``0[])` | `T[]` | hands back, followed: no marker | `new(array)`: the array under another type |
| `M:System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray``1(System.Collections.Immutable.ImmutableArray{``0})` | `ImmutableArray<T>` | hands back, followed: no marker | returns `array.array` |
| `M:System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsMemory``1(System.Collections.Immutable.ImmutableArray{``0}.Builder)` | `ImmutableArray<T>.Builder` | hands back, followed: no marker | returns `builder.AsMemory()` |
| `M:System.Runtime.InteropServices.SequenceMarshal.TryGetReadOnlySequenceSegment``1(System.Buffers.ReadOnlySequence{``0},System.Buffers.ReadOnlySequenceSegment{``0}@,System.Int32@,System.Buffers.ReadOnlySequenceSegment{``0}@,System.Int32@)` | `ReadOnlySequence<T> ; ReadOnlySequenceSegment<T> (by reference) ; ReadOnlySequenceSegment<T> (by reference)` | hands back, followed: no marker | hands what the sequence is made of back through `out`; stores nothing |
| `M:System.Runtime.InteropServices.SequenceMarshal.TryGetArray``1(System.Buffers.ReadOnlySequence{``0},System.ArraySegment{``0}@)` | `ReadOnlySequence<T> ; ArraySegment<T> (by reference)` | hands back, followed: no marker | hands what the sequence is made of back through `out`; stores nothing |
| `M:System.Runtime.InteropServices.SequenceMarshal.TryGetReadOnlyMemory``1(System.Buffers.ReadOnlySequence{``0},System.ReadOnlyMemory{``0}@)` | `ReadOnlySequence<T> ; ReadOnlyMemory<T> (by reference)` | hands back, followed: no marker | hands what the sequence is made of back through `out`; stores nothing |
| `M:System.Nullable.GetValueRefOrDefaultRef``1(System.Nullable{``0}@)` | `Nullable<T> (by reference)` | hands back, followed: no marker | returns `ref nullable.value` |
| `M:System.Buffers.ArrayPool`1.Return(`0[],System.Boolean)` | `T[]` | keeps: no marker | the pool stores the array for a later `Rent` (`SharedArrayPool<T>.Return`, `ConfigurableArrayPool<T>.Return`) |
| `M:System.Nullable`1.op_Explicit(System.Nullable{`0})~`0` | `Nullable<T>` | only uses: no marker (see note) | returns `value.Value`; `borrows` would also let a stored nullable of a non-copyable type be passed by value |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.Threading.Tasks.Task{``0}[])` | `Task<T>[]` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.Threading.Tasks.Task{``0},System.Threading.Tasks.Task{``0})` | `Task<T> ; Task<T>` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.Collections.Generic.IEnumerable{System.Threading.Tasks.Task{``0}})` | `IEnumerable<Task<T>>` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.ReadOnlySpan{System.Threading.Tasks.Task{``0}})` | `ReadOnlySpan<Task<T>>` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |

- only uses: `borrows` already: 7
- keeps: no marker: 15
- only uses: `borrows`: 16
- hands back, followed: no marker: 53
- only uses: no marker (see note): 1
- total: 92

## 10. Channels, the concurrent collections and the keys of a dictionary (2026-11-18)

Added after a trial in which five libraries were written in the ordinary way: about a third of everything that it met was a member of these types that was not listed.

Runtime source read: dotnet/runtime `d757c03be2c39968bca1db2ce9349fce8f1463a3`, the commit that the list names. Every public member of each type is here: 91 are listed, the rest are left out with the reason.

Markers: `new` = what the member hands out is a new value whatever it was called on; `borrows` = the member keeps nothing of a container that it is given; none = what it hands out is as new as what it was called on, so on a stored collection it is a copy and gets a warning (`SCA1030`). `for=TValue` on every entry of the two dictionaries, as for `Dictionary<,>` today.

### System.Threading.Channels

| Member | What it does with a value | Marker |
|---|---|---|
| `Channel.CreateUnbounded<T>()`, `(UnboundedChannelOptions)` | makes a channel; no value | none |
| `Channel.CreateBounded<T>(int)`, `(BoundedChannelOptions)` | makes a channel; no value | none |
| `Channel.CreateBounded<T>(BoundedChannelOptions, Action<T>)` | keeps the callback; a bounded channel in a dropping mode hands each item that it drops to it once (`BoundedChannel.cs` 424, 440) | none |
| `Channel.CreateUnboundedPrioritized<T>()`, `(options)` | orders items with an `IComparer<T>`, which is given two items by value for each comparison | **left out**: a copy of each item goes to the comparer |
| `Channel<TWrite,TRead>.Reader`, `.Writer` | hands out the reader or the writer object | none |
| `Channel<,>` → `ChannelReader<>`, → `ChannelWriter<>` (implicit) | `channel.Reader`, `channel.Writer` | none |
| `ChannelReader<T>.TryRead(out T)` | dequeues: `_items.TryDequeue(out item)` (unbounded), `DequeueItemAndPostProcess()` (bounded). Nothing that another read could hand out is left | `new` |
| `ChannelReader<T>.ReadAsync(ct)` | dequeues, or parks a reader that one writer completes with its item (`TryWrite` → `blockedReader.DangerousSetResult(item)`); a reader that was cancelled is skipped and the item goes to the next or to the buffer | `new` |
| `ChannelReader<T>.TryPeek(out T)` | `_items.TryPeek`, `PeekHead()`: a copy of what the channel still holds | none |
| `ChannelReader<T>.WaitToReadAsync(ct)` | no value | none |
| `ChannelReader<T>.Completion`, `.Count`, `.CanCount`, `.CanPeek` | no value | none |
| `ChannelReader<T>.ReadAllAsync(ct)` | an async iterator: `while (await WaitToReadAsync) while (TryRead(out item)) yield return item;`. Each element is taken out of the channel | **left out**: the sequence is a container, and `new` says nothing of a container. Later marked `drains`: see section 13 |
| `ChannelWriter<T>.TryWrite(T)` | moves the item into one place: the buffer, a parked reader, or the drop callback. On `false` (closed, or full in wait mode) it has not taken it | none |
| `ChannelWriter<T>.WriteAsync(T, ct)` | `TryWrite`, or in a full bounded channel parks the item in one waiting writer, from which a later read moves it into the buffer (`BoundedChannel.cs` 268–273). Cancelled or closed: the item is dropped with the exception | none |
| `ChannelWriter<T>.WaitToWriteAsync(ct)`, `.TryComplete(Exception)`, `.Complete(Exception)` | no value | none |

### System.Collections.Concurrent

| Member | What it does with a value | Marker |
|---|---|---|
| `ConcurrentQueue<T>()` | empty queue | none |
| `ConcurrentQueue<T>.Enqueue(T)` | stores the item in a segment slot | none |
| `ConcurrentQueue<T>.TryDequeue(out T)` | takes the item out of its slot (`ConcurrentQueueSegment.cs` 139) | `new` |
| `ConcurrentQueue<T>.TryPeek(out T)` | a copy of the head | none |
| `ConcurrentQueue<T>.Count`, `.IsEmpty`, `.Clear()` | no value handed out; `Clear` drops what is held | none |
| `ConcurrentQueue<T>.GetEnumerator()` | enumerates a snapshot: each `Current` is a copy of an element that the queue still holds | none |
| `ConcurrentQueue<T>(IEnumerable<T>)`, `.ToArray()`, `.CopyTo(T[], int)` | copy every element | **left out**: copies |
| `ConcurrentStack<T>()`, `.Push(T)` | stores the item in a node | none |
| `ConcurrentStack<T>.TryPop(out T)` | unlinks the head node and hands out its value | `new` |
| `ConcurrentStack<T>.TryPopRange(T[])`, `(T[], int, int)` | unlinks nodes and writes their values into the array (`CopyRemovedItems`); keeps nothing of the array | `borrows` |
| `ConcurrentStack<T>.TryPeek(out T)` | a copy of the head | none |
| `ConcurrentStack<T>.Count`, `.IsEmpty`, `.Clear()`, `.GetEnumerator()` | as for the queue | none |
| `ConcurrentStack<T>(IEnumerable<T>)`, `.PushRange(T[])`, `(T[], int, int)`, `.ToArray()`, `.CopyTo` | copy every element; after `PushRange` the array still holds what the stack now holds too | **left out**: copies |
| `ConcurrentBag<T>()`, `.Add(T)` | stores the item in the thread's queue | none |
| `ConcurrentBag<T>.TryTake(out T)` | `TryLocalPop` or `TrySteal(take: true)`: removes | `new` |
| `ConcurrentBag<T>.TryPeek(out T)` | `TryLocalPeek` or `TrySteal(take: false)`: a copy | none |
| `ConcurrentBag<T>.Count`, `.IsEmpty`, `.Clear()` | no value handed out | none |
| `ConcurrentBag<T>.GetEnumerator()` | `ToArray()` and enumerates that: copies of what the bag still holds | none |
| `ConcurrentBag<T>(IEnumerable<T>)`, `.ToArray()`, `.CopyTo` | copy every element | **left out**: copies |
| `ConcurrentDictionary<,>()`, `(int, int)`, `(IEqualityComparer<TKey>)`, `(int, int, IEqualityComparer<TKey>)` | empty dictionary | none |
| `ConcurrentDictionary<,>.TryAdd(TKey, TValue)` | `TryAddInternal(..., updateIfExists: false)`: stores the value, or returns false and has not taken it | none |
| `ConcurrentDictionary<,>.TryRemove(TKey, out TValue)` | removes the node and hands out its value | `new` |
| `ConcurrentDictionary<,>.TryGetValue(TKey, out TValue)` | a copy of the stored value | none |
| `ConcurrentDictionary<,>.GetOrAdd(TKey, TValue)` | returns the stored value, or stores the argument and returns it: either way a copy of what the dictionary holds afterwards | none |
| `ConcurrentDictionary<,>.this[TKey]` | get: a copy of the stored value. set: stores the value in place of the old one | none |
| `ConcurrentDictionary<,>.ContainsKey(TKey)`, `.Count`, `.IsEmpty`, `.Clear()` | no value handed out | none |
| `ConcurrentDictionary<,>.Keys` | a snapshot of the keys as an `ICollection<TKey>` (`GetKeys`, line 2129): no value is in it | none |
| `ConcurrentDictionary<,>.GetEnumerator()` | each `Current` is a pair with a copy of the stored value | none |
| `ConcurrentDictionary<,>.TryRemove(KeyValuePair<,>)`, `.TryUpdate(TKey, TValue, TValue)` | compare values with `EqualityComparer<TValue>.Default`, which is given a copy of each by value and boxes it for a struct with no `IEquatable<T>` | **left out**: copies |
| `ConcurrentDictionary<,>.AddOrUpdate` (3 overloads) | the update factory is given the stored value by value, and the result is compared as in `TryUpdate` | **left out**: copies |
| `ConcurrentDictionary<,>.GetOrAdd(TKey, Func<TKey,TValue>)`, `GetOrAdd<TArg>(...)` | nothing copies, but what the factory made is dropped when another thread added first, and the result is a second instance as for `GetOrAdd(TKey, TValue)` | **left out**: no use of it with a guard is clean |
| `ConcurrentDictionary<,>.Values`, `.ToArray()`, the 3 constructors from a sequence | copy every value | **left out**: copies |
| `ConcurrentDictionary<,>.GetAlternateLookup`, `.TryGetAlternateLookup` (.NET 9) | hand out a lookup struct with members of its own | **left out**: its members were not read |
| `ConcurrentDictionary<,>.Comparer` | no value | **left out**: not listed for `Dictionary<,>` either |
| `BlockingCollection<T>()`, `(int)` | over a `ConcurrentQueue<T>` of its own | none |
| `BlockingCollection<T>.Add(T)`, `(T, ct)`, `.TryAdd(T)`, `(T, TimeSpan)`, `(T, int)`, `(T, int, ct)` | `_collection.TryAdd(item)` (line 475): moves the item in, or has not taken it (false, or the exception) | none |
| `BlockingCollection<T>.Take()`, `(ct)`, `.TryTake(out T)` and its 3 overloads | `_collection.TryTake(out item)` (line 696): removes | `new` |
| `BlockingCollection<T>.CompleteAdding()`, `.IsAddingCompleted`, `.IsCompleted`, `.Count`, `.BoundedCapacity`, `.Dispose()` | no value; `Dispose` releases the semaphores and leaves what is held | none |
| `BlockingCollection<T>(IProducerConsumerCollection<T>)`, `(..., int)` | wraps another collection | **left out**: what `Add` and `Take` then do is what that collection does |
| `BlockingCollection<T>.AddToAny`, `.TryAddToAny` (6), `.TakeFromAny`, `.TryTakeFromAny` (6) | add to or take from one of several collections | **left out**: the array of collections is a parameter whose type mentions `T`, which needs `borrows`, and the taking ones need `new` as well; one entry carries one marker |
| `BlockingCollection<T>.GetConsumingEnumerable()`, `(ct)` | a sequence that takes each element out as it is enumerated | **left out**: as `ReadAllAsync`. Later marked `drains`: see section 13 |
| `BlockingCollection<T>.ToArray()`, `.CopyTo` | copy every element | **left out**: copies |

### The keys of `Dictionary<TKey, TValue>`

| Member | What it does with a value | Marker |
|---|---|---|
| `Dictionary<,>.Keys` | hands out the key collection, which holds the dictionary | none |
| `KeyCollection.GetEnumerator()`, `.Count`, `.Contains(TKey)`, `.CopyTo(TKey[], int)` | read keys and the count only (`Dictionary.cs` 1900–1937) | none |
| `KeyCollection.Enumerator.MoveNext()`, `.Current`, `.Dispose()` | `_currentKey = entry.key`: no value is read | none |
| `KeyCollection(Dictionary<,>)` | makes a key collection | **left out**: the property is how one is got |

`map.Keys.ToList()` still gets a warning (`SCA1023`): the conversion of the key collection to `IEnumerable<TKey>` loses the value type that its type names, and `erasable` cannot say "for `TValue` only". The clean form is a spread, `[.. map.Keys]`.

**Since this section was written:** `ReadAllAsync` and `GetConsumingEnumerable` are on the list with the `drains` marker. Section
13 has the reading of the source for it. Section 11 took up again some members of `ConcurrentDictionary<,>` that are left out here, such as `Comparer` and the alternate
lookup.

## 11. What low-level code uses (2026-11-18)

Added so that the first hour with the analyzer is not spent on list entries for the framework: what a developer of low-level libraries reaches for with a non-copyable type, collections and beyond. No analyzer logic changed with it.

Runtime source read: dotnet/runtime `d757c03be2c39968bca1db2ce9349fce8f1463a3`, the commit that the list names. Every public member of each type is here, from the .NET 10 reference assemblies. "Entry" is the line that the list has for it now (marker, `for=`), or "out" with the reason.

Rule: listed only if the member makes no copy of a value other than the one it hands out, passes no value to a comparer, Equals, GetHashCode, ToString or a delegate, and does not erase the type.


### System.Collections.Generic.Dictionary`2

| Member | What it does with a value | Entry |
|---|---|---|
| `Dictionary<TKey, TValue>.Dictionary()` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Dictionary(IDictionary<TKey, TValue>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `Dictionary<TKey, TValue>.Dictionary(IDictionary<TKey, TValue>, IEqualityComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `Dictionary<TKey, TValue>.Dictionary(IEnumerable<KeyValuePair<TKey, TValue>>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `Dictionary<TKey, TValue>.Dictionary(IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `Dictionary<TKey, TValue>.Dictionary(IEqualityComparer<TKey>?)` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Dictionary(int)` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Dictionary(int, IEqualityComparer<TKey>?)` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Add(TKey, TValue)` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Clear()` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.ContainsKey(TKey)` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.ContainsValue(TValue)` | EqualityComparer<TValue>.Default.Equals on every value (Dictionary.cs 300-368) | out: passes a value to Equals |
| `Dictionary<TKey, TValue>.EnsureCapacity(int)` | moves the entries into a new array, the old one is dropped (Resize 1248-1290, CopyEntries 1600-1620) | for=TValue |
| `Dictionary<TKey, TValue>.GetAlternateLookup<TAlternateKey>()` | hands out a lookup that holds the dictionary (Dictionary.cs 650-685) | for=TValue |
| `Dictionary<TKey, TValue>.GetEnumerator()` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.GetObjectData(SerializationInfo, StreamingContext)` | serializes or restores every value | out: copies |
| `Dictionary<TKey, TValue>.OnDeserialization(object?)` | serializes or restores every value | out: copies |
| `Dictionary<TKey, TValue>.Remove(TKey)` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Remove(TKey, out TValue)` | listed in an earlier section; see the tables there | new;for=TValue |
| `Dictionary<TKey, TValue>.TrimExcess()` | moves the entries into a new array, the old one is dropped (Resize 1248-1290, CopyEntries 1600-1620) | for=TValue |
| `Dictionary<TKey, TValue>.TrimExcess(int)` | moves the entries into a new array, the old one is dropped (Resize 1248-1290, CopyEntries 1600-1620) | for=TValue |
| `Dictionary<TKey, TValue>.TryAdd(TKey, TValue)` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.TryGetAlternateLookup<TAlternateKey>(out Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>)` | hands out a lookup that holds the dictionary (Dictionary.cs 650-685) | for=TValue |
| `Dictionary<TKey, TValue>.TryGetValue(TKey, out TValue)` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Comparer` | the comparer of keys, or the length of the entries array (Dictionary.cs 160-185) | for=TValue |
| `Dictionary<TKey, TValue>.Count` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Capacity` | the comparer of keys, or the length of the entries array (Dictionary.cs 160-185) | for=TValue |
| `Dictionary<TKey, TValue>.this[TKey]` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Keys` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Values` | a collection that hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `T:System.Collections.Generic.Dictionary`2.AlternateLookup`1` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | view |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.ContainsKey(TAlternateKey)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryAdd(TAlternateKey, TValue)` | stores into a slot found by the alternate key (Dictionary.cs 712-750, 947-960) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TKey, out TValue)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Remove(TAlternateKey)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Remove(TAlternateKey, out TKey, out TValue)` | takes the entry out (Dictionary.cs 872-940) | new;for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Dictionary` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.this[TAlternateKey]` | stores into a slot found by the alternate key (Dictionary.cs 712-750, 947-960) | for=TValue |
| `Dictionary<TKey, TValue>.Enumerator.Dispose()` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Enumerator.MoveNext()` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.Enumerator.Current` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.KeyCollection(Dictionary<TKey, TValue>)` | a view of the keys; judged with Keys in an earlier round | out: a constructor of a view is not what code calls |
| `Dictionary<TKey, TValue>.KeyCollection.CopyTo(TKey[], int)` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.GetEnumerator()` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Contains(TKey)` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Count` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Enumerator.Dispose()` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Enumerator.MoveNext()` | listed in an earlier section; see the tables there | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Enumerator.Current` | listed in an earlier section; see the tables there | for=TValue |
| `T:System.Collections.Generic.Dictionary`2.ValueCollection` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `Dictionary<TKey, TValue>.ValueCollection.ValueCollection(Dictionary<TKey, TValue>)` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `Dictionary<TKey, TValue>.ValueCollection.CopyTo(TValue[], int)` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `Dictionary<TKey, TValue>.ValueCollection.GetEnumerator()` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `Dictionary<TKey, TValue>.ValueCollection.Count` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `T:System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `Dictionary<TKey, TValue>.ValueCollection.Enumerator.Dispose()` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `Dictionary<TKey, TValue>.ValueCollection.Enumerator.MoveNext()` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |
| `Dictionary<TKey, TValue>.ValueCollection.Enumerator.Current` | hands out every value by value, CopyTo included (Dictionary.cs 2081-2244) | out: copies |

### System.Collections.Generic.Queue`1

| Member | What it does with a value | Entry |
|---|---|---|
| `Queue<T>.Queue()` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Queue(IEnumerable<T>)` | copies the source | out: copies |
| `Queue<T>.Queue(int)` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Clear()` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Contains(T)` | compares values with EqualityComparer<T>.Default | out: passes a value to Equals |
| `Queue<T>.CopyTo(T[], int)` | copies every element | out: copies |
| `Queue<T>.Dequeue()` | listed in an earlier section; see the tables there | new |
| `Queue<T>.Enqueue(T)` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.GetEnumerator()` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Peek()` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.ToArray()` | copies every element | out: copies |
| `Queue<T>.TrimExcess()` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.TrimExcess(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.EnsureCapacity(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.TryDequeue(out T)` | listed in an earlier section; see the tables there | new |
| `Queue<T>.TryPeek(out T)` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Count` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Capacity` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.Enumerator.Dispose()` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Enumerator.MoveNext()` | listed in an earlier section; see the tables there | listed |
| `Queue<T>.Enumerator.Current` | listed in an earlier section; see the tables there | listed |

### System.Collections.Generic.Stack`1

| Member | What it does with a value | Entry |
|---|---|---|
| `Stack<T>.Stack()` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Stack(IEnumerable<T>)` | copies the source | out: copies |
| `Stack<T>.Stack(int)` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Clear()` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Contains(T)` | compares values with EqualityComparer<T>.Default | out: passes a value to Equals |
| `Stack<T>.CopyTo(T[], int)` | copies every element | out: copies |
| `Stack<T>.GetEnumerator()` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Peek()` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Pop()` | listed in an earlier section; see the tables there | new |
| `Stack<T>.Push(T)` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.EnsureCapacity(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.ToArray()` | copies every element | out: copies |
| `Stack<T>.TrimExcess()` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.TrimExcess(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.TryPeek(out T)` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.TryPop(out T)` | listed in an earlier section; see the tables there | new |
| `Stack<T>.Count` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Capacity` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.Enumerator.Dispose()` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Enumerator.MoveNext()` | listed in an earlier section; see the tables there | listed |
| `Stack<T>.Enumerator.Current` | listed in an earlier section; see the tables there | listed |

### System.Collections.Generic.PriorityQueue`2

| Member | What it does with a value | Entry |
|---|---|---|
| `PriorityQueue<TElement, TPriority>.PriorityQueue()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(IComparer<TPriority>?)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(IEnumerable<(TElement Element, TPriority Priority)>)` | copies the source | out: copies |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(IEnumerable<(TElement Element, TPriority Priority)>, IComparer<TPriority>?)` | copies the source | out: copies |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(int)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(int, IComparer<TPriority>?)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Clear()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Dequeue()` | takes the root out, or hands back the element that it was given (PriorityQueue.cs 244-400) | new;for=TElement |
| `PriorityQueue<TElement, TPriority>.DequeueEnqueue(TElement, TPriority)` | takes the root out, or hands back the element that it was given (PriorityQueue.cs 244-400) | new;for=TElement |
| `PriorityQueue<TElement, TPriority>.Enqueue(TElement, TPriority)` | moves the element into the heap; the comparer is given priorities only (PriorityQueue.cs 198-221, 736-760) | for=TElement |
| `PriorityQueue<TElement, TPriority>.EnqueueDequeue(TElement, TPriority)` | takes the root out, or hands back the element that it was given (PriorityQueue.cs 244-400) | new;for=TElement |
| `PriorityQueue<TElement, TPriority>.EnqueueRange(IEnumerable<(TElement Element, TPriority Priority)>)` | copies every element of a sequence | out: copies |
| `PriorityQueue<TElement, TPriority>.EnqueueRange(IEnumerable<TElement>, TPriority)` | copies every element of a sequence | out: copies |
| `PriorityQueue<TElement, TPriority>.EnsureCapacity(int)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Peek()` | reads the root by value (PriorityQueue.cs 229-240, 341-355) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Remove(TElement, out TElement, out TPriority, IEqualityComparer<TElement>?)` | compares elements with an IEqualityComparer<TElement> | out: passes a value to a comparer |
| `PriorityQueue<TElement, TPriority>.TrimExcess()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.TryDequeue(out TElement, out TPriority)` | takes the root out, or hands back the element that it was given (PriorityQueue.cs 244-400) | new;for=TElement |
| `PriorityQueue<TElement, TPriority>.TryPeek(out TElement, out TPriority)` | reads the root by value (PriorityQueue.cs 229-240, 341-355) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Comparer` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Count` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Capacity` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.UnorderedItems` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.UnorderedItemsCollection.GetEnumerator()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.UnorderedItemsCollection.Count` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.UnorderedItemsCollection.Enumerator.Dispose()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.UnorderedItemsCollection.Enumerator.MoveNext()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.UnorderedItemsCollection.Enumerator.Current` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |

### System.Collections.Generic.LinkedList`1

| Member | What it does with a value | Entry |
|---|---|---|
| `LinkedList<T>.LinkedList()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.LinkedList(IEnumerable<T>)` | copies the source | out: copies |
| `LinkedList<T>.AddAfter(LinkedListNode<T>, LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddAfter(LinkedListNode<T>, T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddBefore(LinkedListNode<T>, LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddBefore(LinkedListNode<T>, T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddFirst(LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddFirst(T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddLast(LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddLast(T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Clear()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Contains(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | out: passes a value to Equals |
| `LinkedList<T>.CopyTo(T[], int)` | copies every value | out: copies |
| `LinkedList<T>.Find(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | out: passes a value to Equals |
| `LinkedList<T>.FindLast(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | out: passes a value to Equals |
| `LinkedList<T>.GetEnumerator()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.GetObjectData(SerializationInfo, StreamingContext)` | serializes or restores every value | out: copies |
| `LinkedList<T>.OnDeserialization(object?)` | serializes or restores every value | out: copies |
| `LinkedList<T>.Remove(LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Remove(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | out: passes a value to Equals |
| `LinkedList<T>.RemoveFirst()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.RemoveLast()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Count` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.First` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Last` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Enumerator.Dispose()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Enumerator.MoveNext()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Enumerator.Current` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |

### System.Collections.Generic.LinkedListNode`1

| Member | What it does with a value | Entry |
|---|---|---|
| `LinkedListNode<T>.LinkedListNode(T)` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.List` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.Next` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.Previous` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.Value` | reads or assigns the value of the node (LinkedList.cs 620-626) | listed |
| `LinkedListNode<T>.ValueRef` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |

### System.Runtime.CompilerServices.StrongBox`1

| Member | What it does with a value | Entry |
|---|---|---|
| `StrongBox<T>.Value` | a public field | out: a field cannot be an entry; it is reached in place like a field of any object |
| `StrongBox<T>.StrongBox()` | stores the value once (StrongBox.cs) | listed |
| `StrongBox<T>.StrongBox(T)` | stores the value once (StrongBox.cs) | listed |

### System.Collections.Generic.SortedDictionary`2

| Member | What it does with a value | Entry |
|---|---|---|
| `T:System.Collections.Generic.SortedDictionary`2` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.SortedDictionary()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.SortedDictionary(IComparer<TKey>?)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.SortedDictionary(IDictionary<TKey, TValue>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `SortedDictionary<TKey, TValue>.SortedDictionary(IDictionary<TKey, TValue>, IComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `SortedDictionary<TKey, TValue>.Add(TKey, TValue)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Clear()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ContainsKey(TKey)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ContainsValue(TValue)` | EqualityComparer<TValue>.Default.Equals on every value (Dictionary.cs 300-368) | out: passes a value to Equals |
| `SortedDictionary<TKey, TValue>.CopyTo(KeyValuePair<TKey, TValue>[], int)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.GetEnumerator()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Remove(TKey)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Comparer` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Count` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.this[TKey]` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Keys` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Values` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.Enumerator` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Enumerator.Dispose()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Enumerator.MoveNext()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Enumerator.Current` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.KeyCollection` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.KeyCollection(SortedDictionary<TKey, TValue>)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.CopyTo(TKey[], int)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.GetEnumerator()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Contains(TKey)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Count` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.KeyCollection.Enumerator` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Enumerator.Dispose()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Enumerator.MoveNext()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Enumerator.Current` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.ValueCollection` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.ValueCollection(SortedDictionary<TKey, TValue>)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.CopyTo(TValue[], int)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.GetEnumerator()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Count` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.ValueCollection.Enumerator` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Enumerator.Dispose()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Enumerator.MoveNext()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Enumerator.Current` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | out: passes a value to a comparer |

### System.Collections.Generic.SortedList`2

| Member | What it does with a value | Entry |
|---|---|---|
| `SortedList<TKey, TValue>.SortedList()` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.SortedList(IComparer<TKey>?)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.SortedList(IDictionary<TKey, TValue>)` | copies the source | out: copies |
| `SortedList<TKey, TValue>.SortedList(IDictionary<TKey, TValue>, IComparer<TKey>?)` | copies the source | out: copies |
| `SortedList<TKey, TValue>.SortedList(int)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.SortedList(int, IComparer<TKey>?)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Add(TKey, TValue)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Clear()` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.ContainsKey(TKey)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.ContainsValue(TValue)` | compares values (Array.IndexOf, SortedList.cs 425, 651) | out: passes a value to Equals |
| `SortedList<TKey, TValue>.GetEnumerator()` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.GetKeyAtIndex(int)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.GetValueAtIndex(int)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.IndexOfKey(TKey)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.IndexOfValue(TValue)` | compares values (Array.IndexOf, SortedList.cs 425, 651) | out: passes a value to Equals |
| `SortedList<TKey, TValue>.Remove(TKey)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.RemoveAt(int)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.SetValueAtIndex(int, TValue)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.TrimExcess()` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.TryGetValue(TKey, out TValue)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Capacity` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Comparer` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Count` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.this[TKey]` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Keys` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Values` | a list that hands out every value by value, CopyTo included | out: copies |

### System.Collections.Frozen.FrozenDictionary`2

| Member | What it does with a value | Entry |
|---|---|---|
| `FrozenDictionary<TKey, TValue>.ContainsKey(TKey)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.CopyTo(KeyValuePair<TKey, TValue>[], int)` | copies every value | out: copies |
| `FrozenDictionary<TKey, TValue>.CopyTo(Span<KeyValuePair<TKey, TValue>>)` | copies every value | out: copies |
| `FrozenDictionary<TKey, TValue>.GetEnumerator()` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.GetValueRefOrNullRef(TKey)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.GetAlternateLookup<TAlternateKey>()` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.TryGetAlternateLookup<TAlternateKey>(out FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.Comparer` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.Count` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.Empty` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.this[TKey]` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.Keys` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.Values` | copies every value | out: copies |
| `FrozenDictionary<TKey, TValue>.Enumerator.MoveNext()` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.Enumerator.Current` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `T:System.Collections.Frozen.FrozenDictionary`2.AlternateLookup`1` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | view |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.ContainsKey(TAlternateKey)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Dictionary` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.this[TAlternateKey]` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |

### System.Collections.Frozen.FrozenDictionary

| Member | What it does with a value | Entry |
|---|---|---|
| `FrozenDictionary.Create<TKey, TValue>(params ReadOnlySpan<KeyValuePair<TKey, TValue>>)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | out: copies |
| `FrozenDictionary.Create<TKey, TValue>(IEqualityComparer<TKey>?, params ReadOnlySpan<KeyValuePair<TKey, TValue>>)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | out: copies |
| `FrozenDictionary.ToFrozenDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | out: copies |
| `FrozenDictionary.ToFrozenDictionary<TSource, TKey>(IEnumerable<TSource>, Func<TSource, TKey>, IEqualityComparer<TKey>?)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | out: copies |
| `FrozenDictionary.ToFrozenDictionary<TSource, TKey, TElement>(IEnumerable<TSource>, Func<TSource, TKey>, Func<TSource, TElement>, IEqualityComparer<TKey>?)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | out: copies |

### System.Collections.Concurrent.ConcurrentDictionary`2

| Member | What it does with a value | Entry |
|---|---|---|
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary()` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | out: copies |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(IEqualityComparer<TKey>?)` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(int, IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | copies the source | out: copies |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(int, int)` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(int, int, IEqualityComparer<TKey>?)` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AddOrUpdate(TKey, Func<TKey, TValue>, Func<TKey, TValue, TValue>)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | out: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.AddOrUpdate(TKey, TValue, Func<TKey, TValue, TValue>)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | out: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.AddOrUpdate<TArg>(TKey, Func<TKey, TArg, TValue>, Func<TKey, TValue, TArg, TValue>, TArg)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | out: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.Clear()` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ContainsKey(TKey)` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetAlternateLookup<TAlternateKey>()` | hands out a lookup that holds the dictionary | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetEnumerator()` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetOrAdd(TKey, Func<TKey, TValue>)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | out: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.GetOrAdd(TKey, TValue)` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetOrAdd<TArg>(TKey, Func<TKey, TArg, TValue>, TArg)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | out: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.ToArray()` | copies every value | out: copies |
| `ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue)` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryGetAlternateLookup<TAlternateKey>(out ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>)` | hands out a lookup that holds the dictionary | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryRemove(KeyValuePair<TKey, TValue>)` | compares the stored value with EqualityComparer<TValue>.Default (ConcurrentDictionary.cs 413-470, 620-700) | out: passes a value to Equals |
| `ConcurrentDictionary<TKey, TValue>.TryRemove(TKey, out TValue)` | judged in section 10 | new;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryUpdate(TKey, TValue, TValue)` | compares the stored value with EqualityComparer<TValue>.Default (ConcurrentDictionary.cs 413-470, 620-700) | out: passes a value to Equals |
| `ConcurrentDictionary<TKey, TValue>.Comparer` | the comparer of keys | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.Count` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.IsEmpty` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.this[TKey]` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.Keys` | judged in section 10 | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.Values` | copies every value | out: copies |
| `T:System.Collections.Concurrent.ConcurrentDictionary`2.AlternateLookup`1` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | view |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.ContainsKey(TAlternateKey)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryAdd(TAlternateKey, TValue)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TKey, out TValue)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryRemove(TAlternateKey, out TValue)` | unlinks the node and hands out its value (ConcurrentDictionary.cs 2585-2650) | new;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryRemove(TAlternateKey, out TKey, out TValue)` | unlinks the node and hands out its value (ConcurrentDictionary.cs 2585-2650) | new;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Dictionary` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.this[TAlternateKey]` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |

### System.Threading.ThreadPool

| Member | What it does with a value | Entry |
|---|---|---|
| `T:System.Threading.ThreadPool` | not generic | not generic |
| `ThreadPool.BindHandle(nint)` | not generic | not generic |
| `ThreadPool.BindHandle(SafeHandle)` | not generic | not generic |
| `ThreadPool.GetAvailableThreads(out int, out int)` | not generic | not generic |
| `ThreadPool.GetMaxThreads(out int, out int)` | not generic | not generic |
| `ThreadPool.GetMinThreads(out int, out int)` | not generic | not generic |
| `ThreadPool.QueueUserWorkItem(WaitCallback)` | not generic | not generic |
| `ThreadPool.QueueUserWorkItem(WaitCallback, object?)` | not generic | not generic |
| `ThreadPool.QueueUserWorkItem<TState>(Action<TState>, TState, bool)` | keeps the state in the work item and passes it by value to the callback (ThreadPoolWorkQueue.cs 1524-1600) | out: passes a value to a delegate |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, int, bool)` | not generic | not generic |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, long, bool)` | not generic | not generic |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, TimeSpan, bool)` | not generic | not generic |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, uint, bool)` | not generic | not generic |
| `ThreadPool.SetMaxThreads(int, int)` | not generic | not generic |
| `ThreadPool.SetMinThreads(int, int)` | not generic | not generic |
| `ThreadPool.UnsafeQueueNativeOverlapped(NativeOverlapped*)` | not generic | not generic |
| `ThreadPool.UnsafeQueueUserWorkItem(IThreadPoolWorkItem, bool)` | not generic | not generic |
| `ThreadPool.UnsafeQueueUserWorkItem(WaitCallback, object?)` | not generic | not generic |
| `ThreadPool.UnsafeQueueUserWorkItem<TState>(Action<TState>, TState, bool)` | keeps the state in the work item and passes it by value to the callback (ThreadPoolWorkQueue.cs 1524-1600) | out: passes a value to a delegate |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, int, bool)` | not generic | not generic |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, long, bool)` | not generic | not generic |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, TimeSpan, bool)` | not generic | not generic |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, uint, bool)` | not generic | not generic |
| `ThreadPool.CompletedWorkItemCount` | not generic | not generic |
| `ThreadPool.PendingWorkItemCount` | not generic | not generic |
| `ThreadPool.ThreadCount` | not generic | not generic |

### System.Buffers.IBufferWriter`1

| Member | What it does with a value | Entry |
|---|---|---|
| `IBufferWriter<T>.Advance(int)` | hands out memory of the writer to write into (IBufferWriter.cs) | listed |
| `IBufferWriter<T>.GetMemory(int)` | hands out memory of the writer to write into (IBufferWriter.cs) | listed |
| `IBufferWriter<T>.GetSpan(int)` | hands out memory of the writer to write into (IBufferWriter.cs) | listed |

### System.Buffers.ArrayBufferWriter`1

| Member | What it does with a value | Entry |
|---|---|---|
| `ArrayBufferWriter<T>.ArrayBufferWriter()` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.ArrayBufferWriter(int)` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.Advance(int)` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.Clear()` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.ResetWrittenCount()` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.GetMemory(int)` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.GetSpan(int)` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.Capacity` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.FreeCapacity` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.WrittenCount` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.WrittenMemory` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |
| `ArrayBufferWriter<T>.WrittenSpan` | writes into its array, which grows by Array.Resize; hands out views (ArrayBufferWriter.cs) | listed |

### System.Buffers.BuffersExtensions

| Member | What it does with a value | Entry |
|---|---|---|
| `BuffersExtensions.CopyTo<T>(in ReadOnlySequence<T>, Span<T>)` | copies elements | out: copies |
| `BuffersExtensions.PositionOf<T>(in ReadOnlySequence<T>, T)` | compares values with Equals | out: passes a value to Equals |
| `BuffersExtensions.ToArray<T>(in ReadOnlySequence<T>)` | copies elements | out: copies |
| `BuffersExtensions.Write<T>(IBufferWriter<T>, ReadOnlySpan<T>)` | copies elements | out: copies |

### System.Buffers.ReadOnlySequence`1

| Member | What it does with a value | Entry |
|---|---|---|
| `T:System.Buffers.ReadOnlySequence`1` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | view |
| `ReadOnlySequence<T>.Empty` | a static readonly field | out: a field cannot be an entry |
| `ReadOnlySequence<T>.ReadOnlySequence(ReadOnlySequenceSegment<T>, int, ReadOnlySequenceSegment<T>, int)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.ReadOnlySequence(ReadOnlyMemory<T>)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.ReadOnlySequence(T[])` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.ReadOnlySequence(T[], int, int)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.GetEnumerator()` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.GetOffset(SequencePosition)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.GetPosition(long)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.GetPosition(long, SequencePosition)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(int, int)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(int, SequencePosition)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(long)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(long, long)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(long, SequencePosition)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(SequencePosition)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(SequencePosition, int)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(SequencePosition, long)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Slice(SequencePosition, SequencePosition)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.ToString()` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.TryGet(ref SequencePosition, out ReadOnlyMemory<T>, bool)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.End` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.First` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.FirstSpan` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.IsEmpty` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.IsSingleSegment` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Length` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Start` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `T:System.Buffers.ReadOnlySequence`1.Enumerator` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | view |
| `ReadOnlySequence<T>.Enumerator.Enumerator(in ReadOnlySequence<T>)` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Enumerator.MoveNext()` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |
| `ReadOnlySequence<T>.Enumerator.Current` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | listed |

### System.Buffers.ReadOnlySequenceSegment`1

| Member | What it does with a value | Entry |
|---|---|---|
| `ReadOnlySequenceSegment<T>.Memory` | a segment and its memory | listed |
| `ReadOnlySequenceSegment<T>.Next` | a segment and its memory | listed |
| `ReadOnlySequenceSegment<T>.RunningIndex` | a segment and its memory | listed |

### System.Buffers.MemoryManager`1

| Member | What it does with a value | Entry |
|---|---|---|
| `MemoryManager<T>.GetSpan()` | the memory that it manages (MemoryManager.cs) | listed |
| `MemoryManager<T>.Pin(int)` | pins and hands out a handle of its own at every call (MemoryManager.cs) | new |
| `MemoryManager<T>.Unpin()` | the memory that it manages (MemoryManager.cs) | listed |
| `MemoryManager<T>.Memory` | the memory that it manages (MemoryManager.cs) | listed |

### System.Threading.Tasks.Task`1

| Member | What it does with a value | Entry |
|---|---|---|
| `T:System.Threading.Tasks.Task`1` | listed in an earlier section; see the tables there | erasable |
| `Task<TResult>.Task(Func<object?, TResult>, object?)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<object?, TResult>, object?, CancellationToken)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<object?, TResult>, object?, CancellationToken, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<object?, TResult>, object?, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>, CancellationToken)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>, CancellationToken, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.ConfigureAwait(bool)` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.ConfigureAwait(ConfigureAwaitOptions)` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | out: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.GetAwaiter()` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.WaitAsync(CancellationToken)` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.WaitAsync(TimeSpan)` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.WaitAsync(TimeSpan, CancellationToken)` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.WaitAsync(TimeSpan, TimeProvider)` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.WaitAsync(TimeSpan, TimeProvider, CancellationToken)` | listed in an earlier section; see the tables there | listed |
| `Task<TResult>.Factory` | the factory | listed |
| `Task<TResult>.Result` | listed in an earlier section; see the tables there | listed |

### System.Threading.Tasks.TaskFactory`1

| Member | What it does with a value | Entry |
|---|---|---|
| `TaskFactory<TResult>.TaskFactory()` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.TaskFactory(CancellationToken)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.TaskFactory(CancellationToken, TaskCreationOptions, TaskContinuationOptions, TaskScheduler?)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.TaskFactory(TaskCreationOptions, TaskContinuationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.TaskFactory(TaskScheduler?)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAll(Task[], Func<Task[], TResult>)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAll(Task[], Func<Task[], TResult>, CancellationToken)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAll(Task[], Func<Task[], TResult>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAll(Task[], Func<Task[], TResult>, TaskContinuationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>[], TResult>)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>[], TResult>, CancellationToken)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>[], TResult>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>[], TResult>, TaskContinuationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.ContinueWhenAny(Task[], Func<Task, TResult>)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAny(Task[], Func<Task, TResult>, CancellationToken)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAny(Task[], Func<Task, TResult>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAny(Task[], Func<Task, TResult>, TaskContinuationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>, TResult>)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>, TResult>, CancellationToken)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>, TResult>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>, TResult>, TaskContinuationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.FromAsync(Func<AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, object?)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.FromAsync(Func<AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, object?, TaskCreationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.FromAsync(IAsyncResult, Func<IAsyncResult, TResult>)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.FromAsync(IAsyncResult, Func<IAsyncResult, TResult>, TaskCreationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.FromAsync(IAsyncResult, Func<IAsyncResult, TResult>, TaskCreationOptions, TaskScheduler)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, TArg1, object?)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, TArg1, object?, TaskCreationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, TArg1, TArg2, object?)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, TArg1, TArg2, object?, TaskCreationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, TArg1, TArg2, TArg3, object?)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult>, Func<IAsyncResult, TResult>, TArg1, TArg2, TArg3, object?, TaskCreationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | for=TResult |
| `TaskFactory<TResult>.StartNew(Func<object?, TResult>, object?)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.StartNew(Func<object?, TResult>, object?, CancellationToken)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.StartNew(Func<object?, TResult>, object?, CancellationToken, TaskCreationOptions, TaskScheduler)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.StartNew(Func<object?, TResult>, object?, TaskCreationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.StartNew(Func<TResult>)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.StartNew(Func<TResult>, CancellationToken)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.StartNew(Func<TResult>, CancellationToken, TaskCreationOptions, TaskScheduler)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.StartNew(Func<TResult>, TaskCreationOptions)` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.CancellationToken` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.ContinuationOptions` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.CreationOptions` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |
| `TaskFactory<TResult>.Scheduler` | makes a task whose result is what one delegate returns, set once (FutureFactory.cs 507-536, 1296) | listed |

### System.Threading.Tasks.TaskCompletionSource`1

| Member | What it does with a value | Entry |
|---|---|---|
| `T:System.Threading.Tasks.TaskCompletionSource`1` | listed in an earlier section; see the tables there | erasable |
| `TaskCompletionSource<TResult>.TaskCompletionSource()` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TaskCompletionSource(object?)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TaskCompletionSource(object?, TaskCreationOptions)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TaskCompletionSource(TaskCreationOptions)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.SetCanceled()` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.SetCanceled(CancellationToken)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.SetException(IEnumerable<Exception>)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.SetException(Exception)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.SetFromTask(Task<TResult>)` | copies the result of another task into this one | out: copies |
| `TaskCompletionSource<TResult>.SetResult(TResult)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TrySetCanceled()` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TrySetCanceled(CancellationToken)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TrySetException(IEnumerable<Exception>)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TrySetException(Exception)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.TrySetFromTask(Task<TResult>)` | copies the result of another task into this one | out: copies |
| `TaskCompletionSource<TResult>.TrySetResult(TResult)` | listed in an earlier section; see the tables there | listed |
| `TaskCompletionSource<TResult>.Task` | listed in an earlier section; see the tables there | owned |

### System.Threading.Tasks.ValueTask`1

| Member | What it does with a value | Entry |
|---|---|---|
| `ValueTask<TResult>.ValueTask(IValueTaskSource<TResult>, short)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | new |
| `ValueTask<TResult>.ValueTask(Task<TResult>)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `ValueTask<TResult>.ValueTask(TResult)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `ValueTask<TResult>.AsTask()` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.ConfigureAwait(bool)` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.Equals(object?)` | compares the result | out: passes a value to Equals |
| `ValueTask<TResult>.Equals(ValueTask<TResult>)` | compares the result | out: passes a value to Equals |
| `ValueTask<TResult>.GetAwaiter()` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.GetHashCode()` | object members | out: not generic over the value |
| `ValueTask<TResult>.operator ==(ValueTask<TResult>, ValueTask<TResult>)` | compares the result | out: passes a value to Equals |
| `ValueTask<TResult>.operator !=(ValueTask<TResult>, ValueTask<TResult>)` | compares the result | out: passes a value to Equals |
| `ValueTask<TResult>.Preserve()` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.ToString()` | object members | out: not generic over the value |
| `ValueTask<TResult>.IsCanceled` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.IsCompleted` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.IsCompletedSuccessfully` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.IsFaulted` | listed in an earlier section; see the tables there | listed |
| `ValueTask<TResult>.Result` | listed in an earlier section; see the tables there | listed |

### System.Collections.Generic.List`1

| Member | What it does with a value | Entry |
|---|---|---|
| `List<T>.List()` | listed in an earlier section; see the tables there | listed |
| `List<T>.List(IEnumerable<T>)` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.List(int)` | listed in an earlier section; see the tables there | listed |
| `List<T>.Add(T)` | listed in an earlier section; see the tables there | listed |
| `List<T>.AddRange(IEnumerable<T>)` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.AsReadOnly()` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.BinarySearch(int, int, T, IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.BinarySearch(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.BinarySearch(T, IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.Clear()` | listed in an earlier section; see the tables there | listed |
| `List<T>.Contains(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.ConvertAll<TOutput>(Converter<T, TOutput>)` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.CopyTo(int, T[], int, int)` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.CopyTo(T[])` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.CopyTo(T[], int)` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.EnsureCapacity(int)` | listed in an earlier section; see the tables there | listed |
| `List<T>.Exists(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.Find(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindAll(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindIndex(int, int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindIndex(int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindIndex(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindLast(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindLastIndex(int, int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindLastIndex(int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.FindLastIndex(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.ForEach(Action<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.GetEnumerator()` | listed in an earlier section; see the tables there | listed |
| `List<T>.GetRange(int, int)` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.IndexOf(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.IndexOf(T, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.IndexOf(T, int, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.Insert(int, T)` | listed in an earlier section; see the tables there | listed |
| `List<T>.InsertRange(int, IEnumerable<T>)` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.LastIndexOf(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.LastIndexOf(T, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.LastIndexOf(T, int, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.Remove(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.RemoveAll(Predicate<T>)` | passes each element to a delegate | out: passes a value to a delegate |
| `List<T>.RemoveAt(int)` | listed in an earlier section; see the tables there | listed |
| `List<T>.RemoveRange(int, int)` | shifts or swaps elements in place (List.cs 1020-1080) | listed |
| `List<T>.Reverse()` | shifts or swaps elements in place (List.cs 1020-1080) | listed |
| `List<T>.Reverse(int, int)` | shifts or swaps elements in place (List.cs 1020-1080) | listed |
| `List<T>.Slice(int, int)` | copies a range (GetRange) | out: copies |
| `List<T>.Sort()` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.Sort(IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.Sort(Comparison<T>)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.Sort(int, int, IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | out: passes a value to a comparer |
| `List<T>.ToArray()` | copies elements, or wraps the list as an interface that reads by value | out: copies |
| `List<T>.TrimExcess()` | listed in an earlier section; see the tables there | listed |
| `List<T>.TrueForAll(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | out: passes a value to a delegate |
| `List<T>.Capacity` | listed in an earlier section; see the tables there | listed |
| `List<T>.Count` | listed in an earlier section; see the tables there | listed |
| `List<T>.this[int]` | listed in an earlier section; see the tables there | listed |
| `List<T>.Enumerator.Dispose()` | listed in an earlier section; see the tables there | listed |
| `List<T>.Enumerator.MoveNext()` | listed in an earlier section; see the tables there | listed |
| `List<T>.Enumerator.Current` | listed in an earlier section; see the tables there | listed |

### System.Runtime.InteropServices.CollectionsMarshal

| Member | What it does with a value | Entry |
|---|---|---|
| `CollectionsMarshal.AsBytes(BitArray?)` | not generic | out: not generic |
| `CollectionsMarshal.AsSpan<T>(List<T>?)` | listed in an earlier section; see the tables there | listed |
| `CollectionsMarshal.GetValueRefOrNullRef<TKey, TValue>(Dictionary<TKey, TValue>, TKey)` | listed in an earlier section; see the tables there | for=TValue |
| `CollectionsMarshal.GetValueRefOrNullRef<TKey, TValue, TAlternateKey>(Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>, TAlternateKey)` | listed in an earlier section; see the tables there | for=TValue |
| `CollectionsMarshal.GetValueRefOrAddDefault<TKey, TValue>(Dictionary<TKey, TValue>, TKey, out bool)` | listed in an earlier section; see the tables there | for=TValue |
| `CollectionsMarshal.GetValueRefOrAddDefault<TKey, TValue, TAlternateKey>(Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>, TAlternateKey, out bool)` | listed in an earlier section; see the tables there | for=TValue |
| `CollectionsMarshal.SetCount<T>(List<T>, int)` | grows the count over what is past it, which for a T with no reference still holds what was removed or cleared (CollectionsMarshal.cs 140-159) | out: makes an element again of what was removed |

### System.Runtime.CompilerServices.Unsafe

| Member | What it does with a value | Entry |
|---|---|---|
| `Unsafe.AddByteOffset<T>(ref T, nint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.AddByteOffset<T>(ref T, nuint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Add<T>(void*, int)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Add<T>(ref T, int)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Add<T>(ref T, nint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Add<T>(ref T, nuint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.AreSame<T>(ref readonly T, ref readonly T)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.AsPointer<T>(ref readonly T)` | reinterprets the value or the reference | out: erases the type |
| `Unsafe.AsRef<T>(void*)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.AsRef<T>(scoped ref readonly T)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.As<T>(object?)` | reinterprets the value or the reference | out: erases the type |
| `Unsafe.As<TFrom, TTo>(ref TFrom)` | reinterprets the value or the reference | out: erases the type |
| `Unsafe.BitCast<TFrom, TTo>(TFrom)` | reinterprets the value or the reference | out: erases the type |
| `Unsafe.ByteOffset<T>(ref readonly T, ref readonly T)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.CopyBlock(ref byte, ref readonly byte, uint)` | copies a value | out: copies |
| `Unsafe.CopyBlock(void*, void*, uint)` | copies a value | out: copies |
| `Unsafe.CopyBlockUnaligned(ref byte, ref readonly byte, uint)` | copies a value | out: copies |
| `Unsafe.CopyBlockUnaligned(void*, void*, uint)` | copies a value | out: copies |
| `Unsafe.Copy<T>(void*, ref readonly T)` | copies a value | out: copies |
| `Unsafe.Copy<T>(ref T, void*)` | copies a value | out: copies |
| `Unsafe.InitBlock(ref byte, byte, uint)` | writes bytes over memory | out: makes a value that no constructor made |
| `Unsafe.InitBlock(void*, byte, uint)` | writes bytes over memory | out: makes a value that no constructor made |
| `Unsafe.InitBlockUnaligned(ref byte, byte, uint)` | writes bytes over memory | out: makes a value that no constructor made |
| `Unsafe.InitBlockUnaligned(void*, byte, uint)` | writes bytes over memory | out: makes a value that no constructor made |
| `Unsafe.IsAddressGreaterThan<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsAddressGreaterThanOrEqualTo<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsAddressLessThan<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsAddressLessThanOrEqualTo<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsNullRef<T>(ref readonly T)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.NullRef<T>()` | listed in an earlier section; see the tables there | listed |
| `Unsafe.ReadUnaligned<T>(scoped ref readonly byte)` | copies a value | out: copies |
| `Unsafe.ReadUnaligned<T>(void*)` | copies a value | out: copies |
| `Unsafe.Read<T>(void*)` | copies a value | out: copies |
| `Unsafe.SizeOf<T>()` | listed in an earlier section; see the tables there | listed |
| `Unsafe.SkipInit<T>(out T)` | leaves a value that no constructor made | out: not a member that low-level code needs listed |
| `Unsafe.SubtractByteOffset<T>(ref T, nint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.SubtractByteOffset<T>(ref T, nuint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Subtract<T>(void*, int)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Subtract<T>(ref T, int)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Subtract<T>(ref T, nint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Subtract<T>(ref T, nuint)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.Unbox<T>(object)` | listed in an earlier section; see the tables there | listed |
| `Unsafe.WriteUnaligned<T>(ref byte, T)` | copies a value | out: copies |
| `Unsafe.WriteUnaligned<T>(void*, T)` | copies a value | out: copies |
| `Unsafe.Write<T>(void*, T)` | copies a value | out: copies |

### System.Runtime.InteropServices.MemoryMarshal

| Member | What it does with a value | Entry |
|---|---|---|
| `MemoryMarshal.AsBytes<T>(ReadOnlySpan<T>)` | reinterprets the memory | out: erases the type |
| `MemoryMarshal.AsBytes<T>(Span<T>)` | reinterprets the memory | out: erases the type |
| `MemoryMarshal.AsMemory<T>(ReadOnlyMemory<T>)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.AsRef<T>(ReadOnlySpan<byte>)` | reinterprets the memory | out: erases the type |
| `MemoryMarshal.AsRef<T>(Span<byte>)` | reinterprets the memory | out: erases the type |
| `MemoryMarshal.Cast<TFrom, TTo>(ReadOnlySpan<TFrom>)` | reinterprets the memory | out: erases the type |
| `MemoryMarshal.Cast<TFrom, TTo>(Span<TFrom>)` | reinterprets the memory | out: erases the type |
| `MemoryMarshal.CreateFromPinnedArray<T>(T[]?, int, int)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.CreateReadOnlySpanFromNullTerminated(byte*)` | not generic | out: not generic |
| `MemoryMarshal.CreateReadOnlySpanFromNullTerminated(char*)` | not generic | out: not generic |
| `MemoryMarshal.CreateReadOnlySpan<T>(scoped ref readonly T, int)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.CreateSpan<T>(scoped ref T, int)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.GetArrayDataReference(Array)` | not generic | out: not generic |
| `MemoryMarshal.GetArrayDataReference<T>(T[])` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.GetReference<T>(ReadOnlySpan<T>)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.GetReference<T>(Span<T>)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.Read<T>(ReadOnlySpan<byte>)` | copies a value to or from bytes | out: copies |
| `MemoryMarshal.ToEnumerable<T>(ReadOnlyMemory<T>)` | turns a view into a sequence of by-value reads | out: copies |
| `MemoryMarshal.TryGetArray<T>(ReadOnlyMemory<T>, out ArraySegment<T>)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.TryGetMemoryManager<T, TManager>(ReadOnlyMemory<T>, out TManager?)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.TryGetMemoryManager<T, TManager>(ReadOnlyMemory<T>, out TManager?, out int, out int)` | listed in an earlier section; see the tables there | listed |
| `MemoryMarshal.TryGetString(ReadOnlyMemory<char>, out string?, out int, out int)` | not generic | out: not generic |
| `MemoryMarshal.TryRead<T>(ReadOnlySpan<byte>, out T)` | copies a value to or from bytes | out: copies |
| `MemoryMarshal.TryWrite<T>(Span<byte>, in T)` | copies a value to or from bytes | out: copies |
| `MemoryMarshal.Write<T>(Span<byte>, in T)` | copies a value to or from bytes | out: copies |

### System.ArgumentOutOfRangeException

| Member | What it does with a value | Entry |
|---|---|---|
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException()` | holds the value as an object | out: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?)` | holds the value as an object | out: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?, Exception?)` | holds the value as an object | out: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?, object?, string?)` | holds the value as an object | out: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?, string?)` | holds the value as an object | out: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.GetObjectData(SerializationInfo, StreamingContext)` | holds the value as an object | out: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ThrowIfEqual<T>(T, T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual<T>(T, T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfGreaterThan<T>(T, T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfLessThanOrEqual<T>(T, T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfLessThan<T>(T, T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfNegativeOrZero<T>(T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfNegative<T>(T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfNotEqual<T>(T, T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfZero<T>(T, string?)` | compares the value | out: passes a value to a comparison |
| `ArgumentOutOfRangeException.ActualValue` | holds the value as an object | out: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.Message` | holds the value as an object | out: not generic; ActualValue is boxed |

### System.Nullable

| Member | What it does with a value | Entry |
|---|---|---|
| `Nullable.Compare<T>(T?, T?)` | compares the values | out: passes a value to a comparer |
| `Nullable.Equals<T>(T?, T?)` | compares the values | out: passes a value to a comparer |
| `Nullable.GetUnderlyingType(Type)` | no value | out: not generic |
| `Nullable.GetValueRefOrDefaultRef<T>(ref readonly T?)` | listed in an earlier section; see the tables there | listed |

## 12. Members that get at a built-in non-copyable type other than by value (2026-11-19)

A member of an assembly that was not built with the analyzer and that takes a value of one of the ten built-in non-copyable types
by `ref`, `in` or `ref readonly`, in a container, in a view or through a pointer needs a `borrows` entry, or its use gets a warning
(see [Code Not Built With the Analyzer](../../Docs/articles/rules/unchecked-code.md)). `FrameworkScanTests` finds every such member of the
shared frameworks. Each was read in the runtime source; this evidence was first written in the comments of the list, where it
still is:

| Member | What it does with the value | Entry |
|---|---|---|
| `String.Create(IFormatProvider, ref DefaultInterpolatedStringHandler)`, `String.Create(IFormatProvider, Span<char>, ref DefaultInterpolatedStringHandler)` | returns `handler.ToStringAndClear()` (String.cs 352-361 at d757c03) | `borrows` |
| `HybridCache.GetOrCreateAsync` (four overloads with a `ref DefaultInterpolatedStringHandler` key) | passes `key.Text` on and clears the key before it returns (HybridCache.cs 100-139 at d757c03; the two overloads with a `HybridCacheEntryContext` at v11.0.0, 283-320, through `ClearAndReturn`) | `borrows` |
| `JavaMarshal.FinishCrossReferenceProcessing(MarkCrossReferencesArgs*, ReadOnlySpan<GCHandle>)` | pins the span and passes it to the runtime, which destroys each handle in place and keeps nothing (JavaMarshal.cs 61-78, interoplibinterface_java.cpp 111-135 at d757c03). It copies no handle: it ends each one, as `GCHandle.Free` does | `borrows` |

## 13. Sequences that drain, and what `await foreach` binds behind them (2026-11-19)

Runtime source read: dotnet/runtime at the release tags `v10.0.0` and `v11.0.0`, the frameworks that the analyzer is tested with.
Line numbers are the same at both tags unless a second number is given. In `TaskAsyncEnumerableExtensions.cs` and
`ConfiguredCancelableAsyncEnumerable.cs` the two tags differ only in the `#if` around `allows ref struct` (`NET10_0_OR_GREATER` and
`NET`).

#### `drains`

A method is marked `drains` when the sequence that it returns takes each element out of its source as it hands it out, so that no
element is handed out twice by anything (see [Symbol List Format](../../Docs/articles/rules/symbol-list-format.md)).

| Member | What it does with a value | Source | Entry |
|---|---|---|---|
| `ChannelReader<T>.ReadAllAsync(CancellationToken)` | an async iterator: `while (await WaitToReadAsync(...)) while (TryRead(out T? item)) yield return item;`. Each element that it yields is one that `TryRead` has just taken out of the channel, and `TryRead` of every channel of the framework dequeues (section 10). It reads nothing else. No reader of the framework overrides it: `BoundedChannel.cs`, `UnboundedChannel.cs`, `SingleConsumerUnboundedChannel.cs`, `UnboundedPriorityChannel.cs` and `RendezvousChannel.cs` do not mention it | ChannelReader.cs:103-112; `TryRead` is abstract at :36 | `drains` |
| `BlockingCollection<T>.GetConsumingEnumerable()` | `return GetConsumingEnumerable(CancellationToken.None);` | BlockingCollection.cs:1598-1601 (v11.0.0: 1599-1602) | `drains` |
| `BlockingCollection<T>.GetConsumingEnumerable(CancellationToken)` | an iterator: `while (TryTakeWithNoTimeValidation(out T? item, Timeout.Infinite, ...)) yield return item;`. `TryTakeWithNoTimeValidation` waits for the semaphore of occupied nodes and then calls `_collection.TryTake(out item)`, which removes. The two listed constructors make that collection a `ConcurrentQueue<T>`, whose `TryTake` is `TryDequeue` (section 10) | BlockingCollection.cs:1612-1624 (v11.0.0: 1613-1625), :645-696, constructors at :146-147 and :161-162 | `drains` |

What the iterators keep: the compiler keeps the yielded element in the state machine until the next `MoveNext`, so an enumerator
that is driven by hand and read twice at one element hands out a copy, as [Built-in Framework Symbol List](../../Docs/articles/rules/framework-symbol-list.md#sequences-that-drain)
says. The marker describes the sequence as the call returns it.

Two points that the marker rests on, as any entry for a virtual or an interface member does: `ReadAllAsync` is virtual and calls
the abstract `TryRead`, so for a reader of another library it holds as far as that reader's `TryRead`, and any override of
`ReadAllAsync`, do what the documentation of `ReadAllAsync` says: each `MoveNextAsync` that returns `true` "will read the next item
out of the channel" (ChannelReader.cs:99). `GetConsumingEnumerable` takes from the
collection that the `BlockingCollection<T>` wraps; with a collection given to the constructor that takes an
`IProducerConsumerCollection<T>`, which is not on the list, it holds as far as that collection's `TryTake` removes, as
`IProducerConsumerCollection<T>.TryTake` is documented to do.

#### Behind `ConfigureAwait` and `WithCancellation` of an `IAsyncEnumerable<T>`

`await foreach` over `sequence.ConfigureAwait(false)` or `sequence.WithCancellation(ct)` binds these members. Each holds or passes on a
reference to the sequence or to its enumerator and a flag or a token; none touches a value of `T`, except `Current`, which hands out
what the wrapped enumerator hands out. So no entry has a marker: what comes out is as new as what went in.

| Member | What it does with a value | Source | Entry |
|---|---|---|---|
| `TaskAsyncEnumerableExtensions.ConfigureAwait<T>(IAsyncEnumerable<T>, bool)` | `new ConfiguredCancelableAsyncEnumerable<T>(source, continueOnCapturedContext, cancellationToken: default)`: wraps the reference to the sequence | TaskAsyncEnumerableExtensions.cs:24-29 | listed |
| `TaskAsyncEnumerableExtensions.WithCancellation<T>(IAsyncEnumerable<T>, CancellationToken)` | `new ConfiguredCancelableAsyncEnumerable<T>(source, continueOnCapturedContext: true, cancellationToken)`: the same | TaskAsyncEnumerableExtensions.cs:36-41 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.ConfigureAwait(bool)` | a new struct over the same `_enumerable` and `_cancellationToken`, with the new flag. The struct has three fields: the sequence, the token and the flag (:17-19) | ConfiguredCancelableAsyncEnumerable.cs:32-33 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.WithCancellation(CancellationToken)` | a new struct over the same `_enumerable` and flag, with the new token | ConfiguredCancelableAsyncEnumerable.cs:39-40 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.GetAsyncEnumerator()` | `new Enumerator(_enumerable.GetAsyncEnumerator(_cancellationToken), _continueOnCapturedContext)`: calls the sequence's own `GetAsyncEnumerator`, which an `await foreach` over the sequence calls as well, and wraps the enumerator | ConfiguredCancelableAsyncEnumerable.cs:44-47 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.Enumerator.MoveNextAsync()` | `_enumerator.MoveNextAsync().ConfigureAwait(_continueOnCapturedContext)`: a `ValueTask<bool>`; no value of `T`. The enumerator struct has two fields, the enumerator and the flag (:53-54) | ConfiguredCancelableAsyncEnumerable.cs:68-69 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.Enumerator.Current` | `_enumerator.Current`: returns what the wrapped enumerator's `Current` returns, which is the hand-out; it keeps nothing of it | ConfiguredCancelableAsyncEnumerable.cs:72 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.Enumerator.DisposeAsync()` | `_enumerator.DisposeAsync().ConfigureAwait(_continueOnCapturedContext)`: no value of `T` | ConfiguredCancelableAsyncEnumerable.cs:78-79 | listed |

Each reading supports its line. One observation that is not about these lines: neither `ConfiguredCancelableAsyncEnumerable<T>` nor
its `Enumerator` holds a value of `T` inline, but neither is on the list as a `view`, so over a non-copyable `T` both count as
non-copyable structs. That can only add warnings, where one of the two structs is copied, and no line was changed for it.

## 14. Classes that clone themselves (2026-11-19)

The `clones` lines name the classes of the framework whose own code clones an object with `MemberwiseClone`, so that an object of
a class that derives from them is copied field by field. They come from a scan of the IL of the shared frameworks, not from a reading
of the source: every assembly of Microsoft.NETCore.App, Microsoft.AspNetCore.App and Microsoft.WindowsDesktop.App, 10.0.14 and
11.0.0, every method body, every `call`, `callvirt`, `ldftn` and `ldvirtftn` of `Object.MemberwiseClone()` and
`MarshalByRefObject.MemberwiseClone(bool)`, with the receiver read from the instruction before the call. FrameworkScanTests runs the
same scan and fails for a class that it finds and that no line covers.

**What the scan found.** 55 calls in about 40,000 types, the same in both versions. 49 are on `this`, 6 on another object, none on a
receiver that the IL does not show. 22 of the classes that clone `this` can be derived from in another assembly (the definition of
SCA1008, with no `InternalsVisibleTo`, since the framework's grants never reach an application). Two of them are left out on purpose:

- **`System.Delegate`**: C# does not allow a class to derive from it (CS0644), and every delegate type is sealed.
- **`System.MarshalByRefObject`**: the call is the body of `MemberwiseClone(bool)` itself, which clones only when a derived class
  calls it. A call of it in a project gets SCA1005 there, and no class of the framework calls it.

The other 20 are covered by the 11 lines, since a line covers every class that derives from the class it names: the nine XmlSchema
classes that clone themselves derive from `XmlSchemaObject`.

The last two columns come from the same scan, of 11.0.0: the framework methods that call the clone directly (a call through
`ICloneable.Clone` is not counted), and the framework classes that derive from the listed class and can be derived from in turn.

| Line | Assembly | What clones | Framework methods that call it | Derivable framework classes that inherit it |
|---|---|---|---|---|
| `T:System.Text.Encoding;clones` | System.Private.CoreLib | `Clone()`, public, virtual, the `ICloneable` implementation | `Console.InputEncoding` and `Console.OutputEncoding` (the setters), `Encoding.GetEncoding` and `EncodingProvider.GetEncoding` (the overloads with fallbacks), `TextWriterTraceListener`, the constructor of `XmlEncodedRawTextWriter` | 4: ASCIIEncoding, UTF7Encoding, UTF8Encoding, UnicodeEncoding |
| `T:System.Globalization.CultureInfo;clones` | System.Private.CoreLib | `Clone()`, public, virtual; `ReadOnly(CultureInfo)`, public, static, which clones its argument | `Clone`: `X509Certificate.FormatDate` and two internal WPF helpers. `ReadOnly`: `RequestLocalizationMiddleware` of ASP.NET Core, the constructor of `MaskedTextProvider`, the WPF Ribbon | 0 |
| `T:System.Globalization.Calendar;clones` | System.Private.CoreLib | `Clone()`, public, virtual; `ReadOnly(Calendar)`, public, static, which clones its argument | `CultureInfo.Clone`, `DateTimeFormatInfo.Clone`, `CultureInfo.ReadOnly`, `DateTimeFormatInfo.ReadOnly` | 15: GregorianCalendar, JapaneseCalendar, HebrewCalendar and the other calendars |
| `T:System.Xml.Schema.XmlSchemaObject;clones` | System.Private.Xml | `Clone()`, internal, virtual, and the overrides of nine derived classes, each of which clones `this` too | `XmlSchema.DeepClone`, the schema compiler (`SchemaCollectionCompiler.CanonicalizeElement`), the overrides themselves | 57: every public XmlSchema class |
| `T:System.Xml.XmlQualifiedName;clones` | System.Private.Xml | `Clone()`, internal | The `Clone` overrides of the XmlSchema classes | 0 |
| `T:System.Windows.Ink.Stroke;clones` | PresentationCore | `Clone()`, public, virtual | `Stroke.Copy`, `Stroke.Clip`, `Stroke.Erase`, `StrokeCollection.Clone` | 0 |
| `T:System.Windows.Ink.DrawingAttributes;clones` | PresentationCore | `Clone()`, public, virtual | `Stroke.Clone`, `Stroke.DrawCore`, the stroke renderers, `InkCanvas` and the ink collection behavior | 0 |
| `T:System.Drawing.Printing.PageSettings;clones` | System.Drawing.Common | `Clone()`, public, virtual | `PrintController` (the print loops) | 0 |
| `T:System.Drawing.Printing.PrinterSettings;clones` | System.Drawing.Common | `Clone()`, public, virtual | None in the framework: only code that calls it | 0 |
| `T:System.Drawing.Printing.Margins;clones` | System.Drawing.Common | `Clone()`, public, virtual | `PageSettings.Clone` | 0 |
| `T:System.Windows.Forms.GiveFeedbackEventArgs;clones` | System.Windows.Forms | `Clone()`, internal | `DropSource.GiveFeedback` (drag and drop) | 0 |

**The other receivers.** `DateTimeFormatInfo.ReadOnly`, `NumberFormatInfo.ReadOnly` and `TextInfo.ReadOnly` clone their argument as
`Calendar.ReadOnly` and `CultureInfo.ReadOnly` do, but the three classes are sealed. `RuntimeHelpers.GetObjectValue` clones a boxed
struct, and gets SCA1007 where a project calls it. The 27 classes that clone `this` and cannot be derived from are sealed or not public:
the enumerators of the non-generic collections, `Array`, NumberFormatInfo, DateTimeFormatInfo, TextInfo, XmlReaderSettings,
XmlWriterSettings, the Qil nodes of the XSLT compiler and a few internal WPF types.

**A class that replaces fields after the clone is listed all the same.** CultureInfo clones its Calendar, Stroke its DrawingAttributes
and PageSettings its Margins, as the callers above show. That fixes the class's own fields, not those that a
derived class declares, which the clone copies whatever the base class does afterwards.

**The methods that clone have lines of their own** (added 2026-11-19). Each method of a listed class that the scan finds cloning and
that another assembly can call (public, protected, or protected internal) has an `M:...;clones` line: the `Clone` methods of
Encoding, CultureInfo, Calendar, Stroke, DrawingAttributes, PageSettings, PrinterSettings and Margins, and the static `ReadOnly`
methods of CultureInfo and Calendar, 10 lines. A symbol list line can only say that a member makes no copy, so a use of one of
these that the analyzer cannot vouch for gets no advice to list it, and a line that says one makes no copy is left out with
SCA1042. The internal `Clone` methods of the XmlSchema classes, XmlQualifiedName and GiveFeedbackEventArgs cannot be called from a
project and need none. FrameworkScanTests fails for a method that clones and that another assembly can call but has no line.

**The members that clone through other members** (added 2026-11-19). A member with no line of a class whose line says
`clones=complete` is taken not to clone its object, so the scan follows the calls. In the IL of every method from which a call of MemberwiseClone can be reached, it follows
which argument each value on the stack may be, over every path, and works out the arguments that each method may clone and those that
it may return, to a fixed point: a method clones an argument that it gives to an argument of a method that clones that one. A virtual
call on the method's own object reaches the overrides of its class's family, one on another value those of every class that derives
from the called method's class. What a method returns is followed for the methods of the classes whose chain reaches a class that
clones itself, where a member can hand back its object for another one to clone; a method of any other class counts as returning none
of its arguments. Of 357,403 methods of 11.0.0 it follows 52,250, in about 20 seconds, and finds 35 that clone an argument. Besides the
lines above, 7 are public or protected:

| Member | What it clones |
|---|---|
| `Stroke.GetClipResult(IEnumerable<Point>)`, `GetClipResult(Rect)` | The stroke, through the internal `Clip` and `Copy` |
| `Stroke.GetEraseResult(IEnumerable<Point>)`, `GetEraseResult(IEnumerable<Point>, StylusShape)`, `GetEraseResult(Rect)` | The stroke, through the internal `Erase` and `Copy` |
| `Stroke.Draw(DrawingContext, DrawingAttributes)`, `Stroke.DrawCore(DrawingContext, DrawingAttributes)` | The drawing attributes that it is given, through the internal `DrawInternal` |

The other 28 are internal: `Stroke.Clip`, `Copy`, `Erase` and `DrawInternal`, `GiveFeedbackEventArgs.Clone`, `XmlQualifiedName.Clone`,
and the `Clone` and `CloneParticle` methods of the XmlSchema classes. So the XmlSchema classes, XmlQualifiedName and
GiveFeedbackEventArgs have no member line. The members of the Xml classes were not analyzed, so `XmlSchemaObject` and
`XmlQualifiedName` are not marked `clones=complete`, and every member of them and of the XmlSchema classes counts as one that can
clone. A symbol list file can mark them, where its author vouches for their members: a `clones=complete` line counts wherever it
stands, and a plain `clones` line takes nothing away. GiveFeedbackEventArgs is marked `clones=complete`: see section 16.

**The members that can hand back another object of their class** (added 2026-11-19). A member of these classes whose result, or `out`
or `ref` parameter, the object converts to by reference (the class, a class above it, `object`, an interface that it implements, or
a type parameter) could hand back the object itself for another member to clone, and gets the warning unless a line says that it does
not. Besides the `Clone` methods, which have their `clones` lines, 5 such members of the 8 classes with member lines and of the
framework classes that derive from them have a line with no marker, and the scan checks that no path of their IL returns argument 0:

| Line | What it hands back |
|---|---|
| `P:System.Globalization.CultureInfo.Parent` | The parent culture: `InvariantCulture`, or a culture of the cache for the parent's name, never the culture itself |
| `M:System.Globalization.CultureInfo.GetFormat(System.Type)` | `NumberFormat`, `DateTimeFormat` or null |
| `M:System.Globalization.CultureInfo.GetConsoleFallbackUICulture` | A new specific culture for the console's fallback name, kept for the next call |
| `M:System.Windows.Ink.Stroke.GetPropertyData(System.Guid)` | The object that `AddPropertyData` was given for the Guid. Giving it the stroke itself is a conversion to `object` for a member that keeps it, which gets the warning there |
| `M:System.Windows.Ink.DrawingAttributes.GetPropertyData(System.Guid)` | The same, for the drawing attributes |

## 15. Members that receive a value and keep nothing of it (2026-11-19)

A value that is converted to a type which hides it, as the argument of a member of the list, is let through only where the member is
marked "borrows". A line with no marker says how the member treats the values that it holds; it does not say that the member forgets
a reference that it is given, and `List<T>.Add`, `Task.FromResult`, `TaskCompletionSource<T>.SetResult`, `ChannelWriter<T>.TryWrite`,
`Interlocked.Exchange` and the `state` of a task keep theirs. Every member of the list with a parameter that can receive such a
conversion (by value, `in` or `params`, of a type that is `object`, an interface, a class that is not sealed, or a type parameter) was
read: 218, of which 32 keep nothing of what they are given and are now marked "borrows". The other 186 keep the argument, hand it
back, hand out a view or a reference of it, or give it to code that the application can supply, and keep their line with no marker.
The 30 key lookups of the dictionaries are among them: they give the key to the dictionary's comparer, which can keep it.

| Members | Why they keep nothing |
|---|---|
| `Unsafe.AreSame`, `IsAddressGreaterThan`, `IsAddressLessThan`, `IsAddressGreaterThanOrEqualTo`, `IsAddressLessThanOrEqualTo`, `ByteOffset`, `IsNullRef` (7) | They compare addresses or test one, and return a `bool` or an offset |
| `Array.Clear(Array)`, `Array.Clear(Array, int, int)` (2) | They clear the elements in place and return nothing |
| `Object.ReferenceEquals`, `ArgumentNullException.ThrowIfNull(object, string)`, `ObjectDisposedException.ThrowIf(bool, object)`, `GC.KeepAlive`, `RuntimeHelpers.GetHashCode(object)` (5) | They compare, test or read the identity of the object, or keep it alive until the call, and store nothing; `ThrowIf` takes the name of its type for the message |
| The 15 members of `Monitor`: `Enter`, `TryEnter`, `Exit`, `IsEntered`, `Wait`, `Pulse`, `PulseAll` | The lock is a state of the object's header, and no member hands the object to other code |
| `Task.WaitAll(Task[])`, `Task.WaitAll(IEnumerable<Task>, CancellationToken)`, `Task.WaitAny(Task[])` (3) | They wait for the tasks during the call; `WaitAny` returns an index. `WhenAll` and `WhenAny` keep the tasks in the task that they return, and keep their line with no marker |

"borrows" also lets a stored value of a non-copyable type be passed by value to these members, and counts a container that they are
given as used: neither is new for them, since none of them keeps what it is given.

## 16. Members whose own object reaches other code (2026-11-19)

A member line of a class that clones itself says what the member does with its object: a `clones` line says that it clones the
object, or the object that it is given, or hands it to code that can. Where the class's line says `clones=complete`, a member with no
line is taken to do neither. That is the list's statement, and for the built-in list the scan backs it. In the IL of the members of
the classes whose line says `clones=complete` (Encoding, CultureInfo, Calendar, Stroke, DrawingAttributes, PageSettings,
PrinterSettings, Margins, GiveFeedbackEventArgs and the framework classes that derive from them), FrameworkScanTests follows where the object that a member is called on can go, through the methods that it calls, to a fixed
point. The object reaches other code where it is stored in a field, a static, an element or through a reference; given to a method
with no body to read, a delegate's `Invoke` and the constructor of a delegate that captures it included; or given, other than as the
object that the call is made on, to a virtual method that an application can override. The members of the Xml classes were not
analyzed: `XmlSchemaObject` and `XmlQualifiedName` are not marked `clones=complete`, every member of them and of the XmlSchema classes
counts as one that can clone, and the scan leaves them out. GiveFeedbackEventArgs (added 2026-11-20) went through the same scans: its
two public constructors and the ten public accessors of `Effect`, `UseDefaultCursors`, `DragImage`, `CursorOffset` and
`UseDefaultDragImage` clone nothing and hand nothing on, of 11.0.0 and of 10.0.14, so its line says `clones=complete`.

The framework classes that derive from a class whose line says `clones=complete` are covered by the same scan, in whichever assembly a
reference pack puts them: `UTF8Encoding`, `ASCIIEncoding`, `UnicodeEncoding` and `UTF7Encoding` are in System.Text.Encoding.Extensions
there, and `Encoding` in System.Runtime. The 19 such classes, the four encodings and the 15 public calendars, have lines of their own
that say `clones=complete`. FrameworkScanTests fails where those lines differ from what the scan finds, and where a line says
`clones=complete` for a class that the scan does not cover; SafeListTests fails where they differ from the reference packs of .NET 10
and .NET 11. The printing and ink classes and GiveFeedbackEventArgs have no public class that derives from them.

Two things are a judgement written into the scan, not a proof. **The extension points of a family** count as code of the family: the
encoder and decoder fallbacks and their buffers, `Encoder` and `Decoder`, the buffers that an encoding makes for one call, the
`GregorianCalendarHelper` of the calendars based on the Gregorian one, and the `StrokeNodeIterator` and `StrokeInfo` that a stroke
makes to walk its own points. Each exists to serve the class, which hands its object to it by its own design, but an application's own
subclass of a fallback, an `Encoder` or a `Decoder` does receive the object. **A few methods of the runtime** with no body to read are
taken to keep nothing of the object: those of `Monitor`, and `RuntimeHelpers.GetHashCode` and `TryGetHashCode`. Constructors are not
members that an application calls on an existing object, but one can store the new object in another object that a member hands out
later, so the scan follows them too.

Of 11.0.0 and of 10.0.14, the scan flags the same 18 public or protected members and the constructors of three classes. One
constructor leads to a line; the others hand nothing on, for the reasons below:

| Members | What the scan sees, and why it does or does not hand the object on |
|---|---|
| `new PrinterSettings()` | The constructor stores the printer settings in the `PageSettings` that it makes for `DefaultPageSettings`, and `PageSettings.PrinterSettings` hands them back. So `P:System.Drawing.Printing.PrinterSettings.DefaultPageSettings` has a `clones` line: it hands the object to code that can clone it |
| `Stroke.AddPropertyData`, `Stroke.RemovePropertyData`, `Stroke.Transform`, `Stroke.OnDrawingAttributesChanged`, `Stroke.OnDrawingAttributesReplaced`, `Stroke.OnInvalidated`, `Stroke.OnPropertyChanged`, `Stroke.OnPropertyDataChanged`, `Stroke.OnStylusPointsChanged`, `Stroke.OnStylusPointsReplaced`, `DrawingAttributes.OnAttributeChanged`, `DrawingAttributes.OnPropertyChanged`, `DrawingAttributes.OnPropertyDataChanged` | They raise an event of the object with the object as the sender. A handler gets it only where it was subscribed: through the object, which warns as a member whose handler type takes `object` (SCA1009), through a `Stroke`, a `DrawingAttributes` or an `INotifyPropertyChanged` that the object was converted to, which warns there, or by code of the object's own class, which is judged on its own |
| `new Stroke()`, `new DrawingAttributes()`, `Stroke.DrawingAttributes`, `Stroke.StylusPoints` | They subscribe a handler of the object's own to an event of its drawing attributes, its stylus points or its extended properties: the delegate holds the object as its target, and the event of the other object runs only that handler of the object's own class. No member hands out the invocation list of those events |
| `PageSettings.HardMarginX`, `PageSettings.HardMarginY`, `PageSettings.PrintableArea` | They give the page settings to `PrinterSettings.CreateDeviceContext(PageSettings)` or `CreateInformationContext(PageSettings)`, which copy them into a native DEVMODE and keep nothing. The scan tells the overloads of a method apart by their number of parameters only, and sees the overload with an `HGLOBAL` hand its argument to `GlobalLock` |
