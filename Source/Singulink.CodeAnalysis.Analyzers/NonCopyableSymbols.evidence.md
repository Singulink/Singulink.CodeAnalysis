# Evidence for the Framework Symbol List

This file backs the built-in framework symbol list, [NonCopyableSymbols.txt](NonCopyableSymbols.txt), which is built into the
analyzer. For each member on the list it says what in the .NET runtime's source shows that the member makes no copy of a non-copyable
value, and for many members that are not on the list, why they are not. The [Built-in Framework Symbol List](../../Docs/articles/rules/framework-symbol-list.md) article says in plain
words what is on the list, and the [Symbol List Format](../../Docs/articles/rules/symbol-list-format.md) article says what each marker means.

**How the file is organized.** It is organized by subject. Each area of the framework has a section, and each type has one entry in
it: the members that are listed, the members that are not, and the reason for each. The areas are awaiting and tasks; spans, memory
and arrays; marshal and unsafe helpers; atomic helpers; collections; channels and concurrent collections; interfaces; generic structs;
and opaque and untyped sinks. The remaining sections cover the types that are non-copyable or views, the members that receive a
container or a value and keep nothing of it, the static fields of generic types, the classes that clone themselves, and the validation of
the entries. Every table says
what `NonCopyableSymbols.txt` says, and a member that is added to the list or changed gets its evidence in the entry of its type.

**Which runtime source was read.** dotnet/runtime `d757c03be2c39968bca1db2ce9349fce8f1463a3`, as [Runtime Source](#runtime-source) says. The
classes that clone themselves come from a scan of the IL of the shared frameworks instead.

**How to read the tables.**

- A **Listed** column says whether the member is on the list: "listed" is an entry with no marker, "listed, `new`" an entry with that marker, and "not listed" a member that is not on the list.
- A member with no marker hands out what is as new as what it was called on, so on a stored collection it is a copy and gets a warning (`SCA1030`). The markers `new`, `borrows`, `drains`, `owned`, `view`, `noncopyable`, `erasable` and `for=` are the ones that the [Symbol List Format](../../Docs/articles/rules/symbol-list-format.md) article defines. `borrowswhen` is described in [Members That Keep Nothing of a Value in Some Outcomes](#members-that-keep-nothing-of-a-value-in-some-outcomes).
- A **Source** column gives file names and line numbers in the runtime source, with the path abbreviations of [Runtime Source](#runtime-source). A bare `:123-456` is a range of lines in the file that the table or the row names. `×5` is the number of overloads.
- The tables headed "every public member" have a **List entry** column: "listed" for an entry with no marker, the marker (`new`, `view`, `erasable`, `owned`, `for=TValue` and so on) for an entry with one, and "not listed" with the reason for a member that is not on the list.

## Runtime Source

- **Runtime source read:** dotnet/runtime `d757c03be2c39968bca1db2ce9349fce8f1463a3` (branch `main`, 18 July 2025, "Add some additional Tensor APIs (#117739)"). That is the .NET 10 development line, before the .NET 10 release. Everything listed from it also exists in the released .NET 10 and .NET 11 reference assemblies (see [Entry Validation](#entry-validation)), so nothing on the list exists only in unreleased code. The member tables of the collections and of the other types that low-level code reaches for were read at this commit, from the .NET 10 reference assemblies.
- **Other readings of the source:**
  - The `borrows` entries for the handle types and `AsyncFlowControl` are read on dotnet/runtime `main`, 16 November 2026.
  - The `drains` entries and the members behind `ConfigureAwait` and `WithCancellation` of an async sequence are read at the release tags `v10.0.0` and `v11.0.0`, the frameworks that the analyzer is tested with.
  - The `erasable;for=` lines are read at the release tags `v10.0.0` and `v11.0.0`, and in the .NET Framework reference source (see [Key and Value Collections That Are Erasable for the Other Type](#key-and-value-collections-that-are-erasable-for-the-other-type)).
  - The `clones` lines come from a scan of the IL of the shared frameworks, not from a reading of the source (see [Classes That Clone Themselves](#classes-that-clone-themselves)).
- **Path abbreviations in the tables:**
  - `CL/` = `src/libraries/System.Private.CoreLib/src/System/`
  - `CLR/` = `src/coreclr/System.Private.CoreLib/src/System/`
  - `Lib/` = `src/libraries/`
- **Coverage.** The list covers these member by member: the awaiting group, spans and memory, arrays, the marshal classes, `Unsafe`, the pools, `GC`, `Interlocked` and `Volatile`. It covers these **only at a basic level, on purpose**: construct, add, remove, count, clear, indexer and enumeration.
  - `List<T>`, `Stack<T>`, `Queue<T>`, `Dictionary<TKey, TValue>`;
  - the generic collection interfaces;
  - `Nullable<T>`, `KeyValuePair<TKey, TValue>`, `ValueTuple`;
  - the opaque sinks.

  Beyond that basic level, the list covers `LinkedList<T>`, the concurrent collections, `ReadOnlySequence<T>`'s own members, `ManualResetValueTaskSourceCore<T>`'s members and `TaskFactory`, each in its own entry, and the members of `ImmutableArray<T>` that read its length, hand out an element, a reference or a view, and build one, with the counts of `ImmutableList<T>` and `ImmutableDictionary<TKey, TValue>` (see [Default Comparers, an Empty Sequence and Immutable Collections](#default-comparers-an-empty-sequence-and-immutable-collections)). **Not covered:** the other members of the immutable collections, `ContinueWith` and `PoolingAsyncValueTaskMethodBuilder<T>`. A use of one of these members with a non-copyable type gets a warning until someone lists it.

## How the Criterion Is Applied

- **Listed:** the member makes no copy of a `T` value other than what it hands out, by return value, `out` parameter or a container that it returns. It also passes no value to a comparer, `Equals`, `GetHashCode`, `ToString` or a delegate, and does not erase the type.
- **Not listed:** members that copy into a second place that stays reachable, that pass a `T` by value to user code (comparers, `Equals`, `GetHashCode`, `ToString`, delegates), or that erase the type through a reinterpreting cast or a pointer.
- **Internal copies that only end up in what is handed out are part of the hand-out.** `List<T>.Enumerator.MoveNext` copies into `_current`, and `Current` hands that copy out; `ValueTask<T>.ConfigureAwait` copies the whole value task into the awaitable. The rules' own examples need this reading: `foreach (Guard item in CreateGuards())` and `await EnterAsync().ConfigureAwait(false)` are meant to be OK. The entries below say where this reading carries the weight.
- **Rule of thumb that separates `ToArray` from `AsTask`:** a second instance is acceptable only if user code can reach it solely through a by-value read, which then gets a warning on a stored receiver (see [What a Member on the Symbol List Hands Out](../../Docs/articles/rules/listed-members.md)). A result that gives in-place (`ref`) access to the second instance, such as an array, is never acceptable.

### Rules That the Entries Rest On

1. **One entry covers every type parameter of a member,** unless `for=` limits it. A member that is safe for `TValue` but compares `TKey`, as every `Dictionary` lookup does, takes `for=TValue`.
2. **A copy inside what is handed out is allowed** when every way to get at it is a by-value read through a listed member, which the analyzer judges by where the container came from. Enumerators, `ValueTask<T>.ConfigureAwait`, `AsTask` and `Task<T>.WaitAsync` all keep a second instance internally and are listed under this rule.
3. **A container built from stored containers is as new as what is put into it.** `Task.WhenAny<T>(t1, t2)` with stored `t1` and `t2` takes a `params` array created at the call, so the result follows the tasks that are passed.
4. **The analyzer does not examine the calls that the compiler generates in an `async` method.** The async method builders have no entries for that reason (see [Async Method Builders](#async-method-builders)).
5. **The non-generic members that a typed `foreach` needs are listed.** `IEnumerator.MoveNext` and `IDisposable.Dispose` are reached on an `IEnumerator<Guard>` through a non-generic interface, which would be a conversion to a type that hides the non-copyable type.
6. **`Interlocked` and `Volatile` entries can only apply to containers** (see [Atomic Helpers](#atomic-helpers)). `Interlocked.Exchange` throws for a struct that is not a primitive or an enum, so its `new` marker serves a container, out of whose location it takes the reference.

## Awaiting and Tasks

The members that make, complete, await and read tasks and value tasks.

### `Task<T>`

`CL/Threading/Tasks/Future.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `Result` | listed | Returns `m_result`; the hand-out | Future.cs:428-465 |
| `GetAwaiter()` | listed | Wraps `this` in a `TaskAwaiter<T>` (a view) | Future.cs:506 |
| `ConfigureAwait(bool)`, `ConfigureAwait(ConfigureAwaitOptions)` | listed | Wraps `this` | Future.cs:517, 527 |
| `WaitAsync` ×5 | listed | The promise that it returns copies the result: two live instances, with the copy reachable only by a by-value read through a listed member (see below) | Future.cs:549-616, Task.cs:2925 |
| `ContinueWith` ×20 | not listed | Gives the antecedent task, not its result, to a delegate, which reads the result where it likes | Future.cs:631-1375 |
| constructors with `Func<T>` | listed | Keep what the delegate returns as the result, once, as `Task.Run` does; the state is an object of the caller's | Future.cs:107-271 |

- **`WaitAsync` (five overloads) is listed.** Unless it can return `this`, it returns a `CancellationPromise<T>` (`CL/Threading/Tasks/Future.cs:599-616`). On completion that promise does `TrySetResult(taskTResult.Result)` (`CL/Threading/Tasks/Task.cs:2925`), so the original task and the promise both hold the result. Under the rule of thumb in [How the Criterion Is Applied](#how-the-criterion-is-applied) this is acceptable: the original task gives access only by value, and the only way to get at the copy is a by-value read through a listed member, which the analyzer judges by where the task came from. This is the same shape as `ValueTask<T>.AsTask`. The generic `Task.WhenAll<TResult>` is not listed because it hands out copies in an array, whose elements can be used in place.
- **The non-generic `Task.WaitAsync` overloads are listed:** with `TResult = VoidTaskResult` they copy nothing (`Task.cs:2849`).
- **`Task<T>` is marked `erasable`.** Its base type `Task` and its interfaces `IAsyncResult` and `IDisposable` expose nothing of `TResult`, so a conversion to them is a dead end. This is read in the runtime source.
- **The constructors keep what the delegate returns as the result, once,** as `Task.Run` does. `Task<T>.Factory` is listed.
- **`ContinueWith` is not listed** (×20, see the table of every public member): the delegate reads the result where it likes.

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `T:System.Threading.Tasks.Task`1` | the type is `erasable`: see above | erasable |
| `Task<TResult>.Task(Func<object?, TResult>, object?)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<object?, TResult>, object?, CancellationToken)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<object?, TResult>, object?, CancellationToken, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<object?, TResult>, object?, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>, CancellationToken)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>, CancellationToken, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.Task(Func<TResult>, TaskCreationOptions)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `Task<TResult>.ConfigureAwait(bool)` | a basic member: see the summary table | listed |
| `Task<TResult>.ConfigureAwait(ConfigureAwaitOptions)` | a basic member: see the summary table | listed |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>, object?>, object?, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith(Action<Task<TResult>>, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, object?, TNewResult>, object?, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, CancellationToken)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, CancellationToken, TaskContinuationOptions, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, TaskContinuationOptions)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult>, TaskScheduler)` | gives the antecedent task, not its result, to a delegate; the delegate reads the result by value through Result | not listed: passes the task to a delegate; the result is read where the delegate reads it |
| `Task<TResult>.GetAwaiter()` | a basic member: see the summary table | listed |
| `Task<TResult>.WaitAsync(CancellationToken)` | a basic member: see the summary table | listed |
| `Task<TResult>.WaitAsync(TimeSpan)` | a basic member: see the summary table | listed |
| `Task<TResult>.WaitAsync(TimeSpan, CancellationToken)` | a basic member: see the summary table | listed |
| `Task<TResult>.WaitAsync(TimeSpan, TimeProvider)` | a basic member: see the summary table | listed |
| `Task<TResult>.WaitAsync(TimeSpan, TimeProvider, CancellationToken)` | a basic member: see the summary table | listed |
| `Task<TResult>.Factory` | the factory | listed |
| `Task<TResult>.Result` | a basic member: see the summary table | listed |

### `Task` Statics

`CL/Threading/Tasks/Task.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `FromResult<T>(T)` | listed | Moves the argument into a new task; it can hand out a shared cached task (see below) | Task.cs:5386 |
| `FromCanceled<T>`, `FromException<T>` | listed | No `T` value | Task.cs:5479, 5455 |
| `Run<T>(Func<T>)`, `Run<T>(Func<T>, CancellationToken)` | listed | `m_result = func()`, a move from checked code | Task.cs:5557, 5575; Future.cs:490 |
| `Run<T>(Func<Task<T>>)` ×2 | not listed | `UnwrapPromise<T>` does `TrySetResult(taskTResult.Result)` while the inner task, which the delegate may return from a field, keeps its result | Task.cs:5636-5668, 7552 |
| `WhenAll<T>` ×3 | not listed | Copies every result into a `T[]` while the tasks keep theirs | Task.cs:6252, 6335, 6373 |
| `WhenAny<T>` ×4 | listed | Copies no result; hands back one of the argument tasks, so its result follows the tasks that are passed | Task.cs:6811-6865 |
| `WhenEach<T>` ×3 | not listed | Hands back an argument task | Task.cs:6898-6913 |

- **`Task.FromResult<T>` is listed, but it can hand out a shared cached task.**
  - For a reference-free struct of 1, 2, 4, 8 or 16 bytes whose bits are all zero, it returns `Task<T>.s_defaultResultTask` (`Task.cs:5394-5430`). The same goes for a `null` `Nullable<T>`.
  - Several callers then read "new" results from one task. Each read is a by-value copy of `default`, which is no different from writing `default`, so it is listed.
  - `AsyncTaskMethodBuilder<T>.SetResult` goes through this path (`CL/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs:473`).
- **`Task.Run<T>(Func<T>)` is not marked `borrows`.** It keeps the delegate in the task until it has run (`Task<TResult>.StartNew(null, function, ...)`), so the member keeps what it is given.
- **Generic `WhenAll<TResult>` is not listed.**
  - It gathers every result into a new `TResult[]` while the tasks keep theirs (`Task.cs:6252-6380`), and an array gives in-place access to the copies.
  - `await Task.WhenAll(first, second)` with two `Task<Guard>` arguments binds to `WhenAll<Guard>(params Task<Guard>[])`, not to `WhenAll(params Task[])`. It reaches the non-generic overload only with a cast to `Task`.
- **The generic `Task.WhenAny<TResult>` (4 overloads) is listed with no marker.** `WhenAny<TResult>` copies no result, but it hands back one of the argument tasks. A `params` array built from stored `Task<Guard>` locals is a creation expression, and a created array or collection is as new as what is put into it, so the result follows the tasks that are passed: stored tasks give a stored result. The generic `WhenEach<TResult>` hands back an argument task as well and is not listed.
- **The non-generic `Task.WhenAll` and `Task.WhenAny` overloads are listed:** what they return is a `Task`, and reading a result from it needs a cast, which gets a warning (see [`Task` Members That Do Not Depend on the Result](#task-members-that-do-not-depend-on-the-result)).

### Awaiters and Awaitables

`CL/Runtime/CompilerServices/`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `TaskAwaiter<T>`: `IsCompleted`, `GetResult`, `OnCompleted`, `UnsafeOnCompleted` | listed | `GetResult` returns `m_task.ResultOnSuccess`; the rest only look at the task | TaskAwaiter.cs:305-336 |
| `ConfiguredTaskAwaitable<T>.GetAwaiter` | listed | Wraps the task | TaskAwaiter.cs:454 |
| `ConfiguredTaskAwaitable<T>.ConfiguredTaskAwaiter`: the same four members | listed | As for `TaskAwaiter<T>` | TaskAwaiter.cs:485-516 |
| `ValueTaskAwaiter<T>`: the same four members | listed | `GetResult` returns `_value.Result`; the others read `_obj` only | ValueTaskAwaiter.cs:118-179 |
| `ConfiguredValueTaskAwaitable<T>.GetAwaiter` | listed | `new ConfiguredValueTaskAwaiter(in _value)`: a copy that is the hand-out | ConfiguredValueTaskAwaitable.cs:131 |
| `ConfiguredValueTaskAwaitable<T>.ConfiguredValueTaskAwaiter`: the same four members | listed | As for `ValueTaskAwaiter<T>` | ConfiguredValueTaskAwaitable.cs:146-215 |

`TaskAwaiter<T>`, `ConfiguredTaskAwaitable<T>` and its awaiter hold only a `Task<T>` reference, so they are views (see [Views](#views)). `ValueTaskAwaiter<T>` and the `ConfiguredValueTaskAwaitable<T>` types are **not** views: they hold a `ValueTask<T>`, which stores the result inline.

### `ValueTask<T>` and `ValueTask`

`CL/Threading/Tasks/ValueTask.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `#ctor(T)`, `#ctor(Task<T>)`, `#ctor(IValueTaskSource<T>, short)` | listed; the third has `new` | Store the argument; the value task of the third is the one handle to the result of its source and token (see below) | ValueTask.cs:482-523 |
| `AsTask()` | listed | Inline result: `Task.FromResult(_result)`, the hand-out; source: `GetResult` moves the result into the task; task: returns it | ValueTask.cs:573-680 |
| `Preserve()` | listed | `this`, or a value task over `AsTask()` | ValueTask.cs:592 |
| `IsCompleted`, `IsCompletedSuccessfully`, `IsFaulted`, `IsCanceled` | listed | Read `_obj` only | ValueTask.cs:702-795 |
| `Result` | listed | The hand-out | ValueTask.cs:798-818 |
| `GetAwaiter()` | listed | `new ValueTaskAwaiter<TResult>(in this)` | ValueTask.cs:823 |
| `ConfigureAwait(bool)` | listed | Copies the fields into the awaitable; an example of the rules | ValueTask.cs:831 |
| `Equals(ValueTask<T>)`, `Equals(object)`, `==`, `!=` | not listed | `EqualityComparer<TResult>.Default.Equals(_result, …)` | ValueTask.cs:548-564 |
| `GetHashCode()` | not listed | `_result.GetHashCode()` on a read-only field: defensive copy | ValueTask.cs:542 |
| `ToString()` | not listed | `TResult result = Result;` then `ToString` | ValueTask.cs:835 |
| `ValueTask.FromResult<T>`, `FromCanceled<T>`, `FromException<T>` | listed | Construct a value task | ValueTask.cs:119-144 |

- **`ValueTask<T>.#ctor(IValueTaskSource<T>, short)` is marked `new`.** With no marker, the task would count as stored whenever the source is, which would give a warning on every implementation of a value-task source. Two tasks from one source and token would each count as new; the contract of `IValueTaskSource<T>` forbids that. The source itself stays stored.

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `ValueTask<TResult>.ValueTask(IValueTaskSource<TResult>, short)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | new |
| `ValueTask<TResult>.ValueTask(Task<TResult>)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `ValueTask<TResult>.ValueTask(TResult)` | keeps what the delegate returns as the result (Future.cs 85-93, 490-496) | listed |
| `ValueTask<TResult>.AsTask()` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.ConfigureAwait(bool)` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.Equals(object?)` | compares the result | not listed: passes a value to Equals |
| `ValueTask<TResult>.Equals(ValueTask<TResult>)` | compares the result | not listed: passes a value to Equals |
| `ValueTask<TResult>.GetAwaiter()` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.GetHashCode()` | object members | not listed: not generic over the value |
| `ValueTask<TResult>.operator ==(ValueTask<TResult>, ValueTask<TResult>)` | compares the result | not listed: passes a value to Equals |
| `ValueTask<TResult>.operator !=(ValueTask<TResult>, ValueTask<TResult>)` | compares the result | not listed: passes a value to Equals |
| `ValueTask<TResult>.Preserve()` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.ToString()` | object members | not listed: not generic over the value |
| `ValueTask<TResult>.IsCanceled` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.IsCompleted` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.IsCompletedSuccessfully` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.IsFaulted` | a basic member: see the summary table | listed |
| `ValueTask<TResult>.Result` | a basic member: see the summary table | listed |

### `IValueTaskSource<T>`

| Member | Listed | Reason |
|---|---|---|
| `GetResult(short)` | listed | Specified to hand out the result |
| `GetStatus(short)`, `OnCompleted(...)` | listed | No `T` value |

### `ManualResetValueTaskSourceCore<T>`

It is a `noncopyable` struct: a mutable struct holding `_result` and the continuation, meant to live in one field (see [Non-copyable Types](#non-copyable-types)).

The list has eight members of it: `RunContinuationsAsynchronously`, `Version`, `Reset`, `SetResult`, `SetException`, `GetStatus`, `GetResult` and `OnCompleted`. `GetResult` has no marker on purpose: it returns the result and leaves it in the core until `Reset`, so a by-value read from a stored core gets a warning. `SetResult` stores the value it is given, which is a move in. The entries are read in the runtime source.

### `TaskCompletionSource<T>`

`CL/Threading/Tasks/TaskCompletionSource_T.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| 4 constructors, `Task` | listed; `Task` has `owned` | Create or return the task; the task belongs to the source alone (see below) | :37-76 |
| `SetResult`, `TrySetResult` | listed, and `borrowswhen=false` on `TrySetResult` | `_task.TrySetResult(result)` stores it (`Future.cs:371`) | :197, 216 |
| `SetException` ×2, `TrySetException` ×2, `SetCanceled` ×2, `TrySetCanceled` ×2 | listed | No `T` value | :87-280 |
| `SetFromTask`, `TrySetFromTask` | not listed | `_task.TrySetResult(completedTask.Result)`: two tasks hold the value | :305-336 |

- **`TaskCompletionSource<T>.Task` is marked `owned`, and `TaskCompletionSource<T>` is marked `erasable`.** Read in `TaskCompletionSource_T.cs` and `Future.cs` at the pinned commit: the task is a private readonly field that only the four constructors assign, `Task` is the only member that hands it out, and the class has no base class, no interface and no override of a member of `object`.
- **`SetFromTask` and `TrySetFromTask` are not listed.** They copy the result of another task into the source (`_task.TrySetResult(completedTask.Result)`: two tasks hold the value) and must stay off the list while `Task` is `owned`. A `Task<TResult>` gives its result by value only.

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `T:System.Threading.Tasks.TaskCompletionSource`1` | the type is `erasable`: see above | erasable |
| `TaskCompletionSource<TResult>.TaskCompletionSource()` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TaskCompletionSource(object?)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TaskCompletionSource(object?, TaskCreationOptions)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TaskCompletionSource(TaskCreationOptions)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.SetCanceled()` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.SetCanceled(CancellationToken)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.SetException(IEnumerable<Exception>)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.SetException(Exception)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.SetFromTask(Task<TResult>)` | copies the result of another task into this one | not listed: copies |
| `TaskCompletionSource<TResult>.SetResult(TResult)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TrySetCanceled()` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TrySetCanceled(CancellationToken)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TrySetException(IEnumerable<Exception>)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TrySetException(Exception)` | a basic member: see the summary table | listed |
| `TaskCompletionSource<TResult>.TrySetFromTask(Task<TResult>)` | copies the result of another task into this one | not listed: copies |
| `TaskCompletionSource<TResult>.TrySetResult(TResult)` | a basic member: see the summary table; takes the value only where it returns true (see [Members That Keep Nothing of a Value in Some Outcomes](#members-that-keep-nothing-of-a-value-in-some-outcomes)) | borrowswhen=false |
| `TaskCompletionSource<TResult>.Task` | a basic member: see the summary table | owned |

### Async Method Builders

`CL/Runtime/CompilerServices/`

None of the async method builder members is listed. The analyzer does not examine the calls that the compiler generates in an `async` method, so entries would have no use, and the builders keep a private copy of the result:

- `AsyncValueTaskMethodBuilder<T>.Task` returns `new ValueTask<TResult>(_result)` and leaves `_result` in the builder (`CL/Runtime/CompilerServices/AsyncValueTaskMethodBuilderT.cs:70`). The builder is a private field of the compiler's state machine and is dropped with it, so no user code can reach the second instance.
- `AwaitOnCompleted`/`AwaitUnsafeOnCompleted` copy the state machine into a heap box. That is the compiler's own move.

What each member does:

| Member | What it does | Source |
|---|---|---|
| `AsyncValueTaskMethodBuilder<T>`: `Create`, `Start`, `SetStateMachine`, `SetException`, `AwaitOnCompleted`, `AwaitUnsafeOnCompleted` | No `T` value; awaiters are used through `ref` | AsyncValueTaskMethodBuilderT.cs:29-102 |
| `AsyncValueTaskMethodBuilder<T>`: `SetResult`, `Task` | Store, then hand out | AsyncValueTaskMethodBuilderT.cs:45-70 |
| `AsyncTaskMethodBuilder<T>`: the same eight members | `SetResult` goes through `FromResult` or `TrySetResult`; awaiter used in place | AsyncTaskMethodBuilderT.cs:27-100, 428-480 |

### `Task` Members That Do Not Depend on the Result

`CL/Threading/Tasks/Task.cs`. These are the members of the non-generic `Task` that code uses on a `Task<T>` too.

| Member | Listed | Reason | Source |
|---|---|---|---|
| `Task`: `IsCompleted`, `IsCompletedSuccessfully`, `IsFaulted`, `IsCanceled`, `Status`, `Exception`, `Id`, `AsyncState`, `CreationOptions`, `Dispose` | listed | State flags and contingent properties; no result | CL/Threading/Tasks/Task.cs:1190-1579 |
| `Task.Wait` ×6 | listed | Waits; no result | Task.cs:2623-2754 |
| `Task.WaitAsync` ×5 (non-generic) | listed | `CancellationPromise<VoidTaskResult>`; no result copy | Task.cs:2795-2849 |
| `Task.GetAwaiter`, `Task.ConfigureAwait` ×2 | listed | Awaiting a `Task<T>` as a `Task` | |
| `Task.WhenAll(Task[])`, `(IEnumerable<Task>)`, `(ReadOnlySpan<Task>)` | listed | No result is read | Task.cs:5929-6250 |
| `Task.WhenAny(Task[])`, `(Task, Task)`, `(IEnumerable<Task>)`, `(ReadOnlySpan<Task>)` | listed | Returns `Task<Task>`; reaching the result needs a cast | Task.cs:6525-6717 |
| `Task.WaitAll` ×7, `Task.WaitAny` ×5 | listed | Wait only. `WaitAll(Task[])`, `WaitAll(IEnumerable<Task>, CancellationToken)` and `WaitAny(Task[])` are marked `borrows`: they wait for the tasks during the call, and `WaitAny` returns an index | Task.cs:4703-5314 |
| `Task.WhenEach(Task…)` ×3 | not listed | Not needed | Task.cs:6879-6892 |

`WhenAll` and `WhenAny` keep the tasks in the task that they return, and keep their line with no marker. The generic overloads are in [`Task` Statics](#task-statics).

### `TaskFactory<TResult>`

Each member makes a task whose result is what one delegate returns, set once. What it is given of other tasks is judged by their own type parameters.

| Member | What it does with a value | List entry |
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

### `ThreadPool`

Only `QueueUserWorkItem<TState>` and `UnsafeQueueUserWorkItem<TState>` are generic, and neither is listed. The other members are not generic.

| Member | What it does with a value | List entry |
|---|---|---|
| `T:System.Threading.ThreadPool` | not generic | not listed: not generic |
| `ThreadPool.BindHandle(nint)` | not generic | not listed: not generic |
| `ThreadPool.BindHandle(SafeHandle)` | not generic | not listed: not generic |
| `ThreadPool.GetAvailableThreads(out int, out int)` | not generic | not listed: not generic |
| `ThreadPool.GetMaxThreads(out int, out int)` | not generic | not listed: not generic |
| `ThreadPool.GetMinThreads(out int, out int)` | not generic | not listed: not generic |
| `ThreadPool.QueueUserWorkItem(WaitCallback)` | not generic | not listed: not generic |
| `ThreadPool.QueueUserWorkItem(WaitCallback, object?)` | not generic | not listed: not generic |
| `ThreadPool.QueueUserWorkItem<TState>(Action<TState>, TState, bool)` | keeps the state in the work item and passes it by value to the callback (ThreadPoolWorkQueue.cs 1524-1600) | not listed: passes a value to a delegate |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, int, bool)` | not generic | not listed: not generic |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, long, bool)` | not generic | not listed: not generic |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, TimeSpan, bool)` | not generic | not listed: not generic |
| `ThreadPool.RegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, uint, bool)` | not generic | not listed: not generic |
| `ThreadPool.SetMaxThreads(int, int)` | not generic | not listed: not generic |
| `ThreadPool.SetMinThreads(int, int)` | not generic | not listed: not generic |
| `ThreadPool.UnsafeQueueNativeOverlapped(NativeOverlapped*)` | not generic | not listed: not generic |
| `ThreadPool.UnsafeQueueUserWorkItem(IThreadPoolWorkItem, bool)` | not generic | not listed: not generic |
| `ThreadPool.UnsafeQueueUserWorkItem(WaitCallback, object?)` | not generic | not listed: not generic |
| `ThreadPool.UnsafeQueueUserWorkItem<TState>(Action<TState>, TState, bool)` | keeps the state in the work item and passes it by value to the callback (ThreadPoolWorkQueue.cs 1524-1600) | not listed: passes a value to a delegate |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, int, bool)` | not generic | not listed: not generic |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, long, bool)` | not generic | not listed: not generic |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, TimeSpan, bool)` | not generic | not listed: not generic |
| `ThreadPool.UnsafeRegisterWaitForSingleObject(WaitHandle, WaitOrTimerCallback, object?, uint, bool)` | not generic | not listed: not generic |
| `ThreadPool.CompletedWorkItemCount` | not generic | not listed: not generic |
| `ThreadPool.PendingWorkItemCount` | not generic | not listed: not generic |
| `ThreadPool.ThreadCount` | not generic | not listed: not generic |

## Spans, Memory and Arrays

Views over memory, arrays, the pools and the buffer types. Whether each view is a `view` struct or not is in [Non-copyable and View Types](#non-copyable-and-view-types).

### `Span<T>` and `ReadOnlySpan<T>`

`CL/Span.cs`, `CL/ReadOnlySpan.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| constructors from an array, a pointer and a reference | listed | Store a reference and a length | Span.cs:41-127; ReadOnlySpan.cs:41-121 |
| `Item`, `Length`, `IsEmpty`, `Empty`, `GetPinnableReference` | listed | `ref` or a count | Span.cs:146-221, 282 |
| `==`, `!=` | listed, `borrows` | Compare reference and length; they only use the spans | Span.cs:183, 364 |
| implicit conversions from `T[]` and `ArraySegment<T>`, and `Span<T>` to `ReadOnlySpan<T>` | listed | Build a view | Span.cs:210-215, 371 |
| `GetEnumerator`, `Enumerator.MoveNext`, `Enumerator.Current` | listed | `Current` is `ref _span[_index]` | Span.cs:224-264 |
| `Slice` ×2 | listed | A view | Span.cs:395, 412 |
| `Clear` | listed | Zeroes elements: drops, no copy | Span.cs:294-306 |
| `ToString` | listed | For `T` other than `char`: type name and length | Span.cs:378-388 |
| `Fill`, `CopyTo`, `TryCopyTo`, `ToArray` | not listed | Duplicate | Span.cs:310-437 |
| `Equals(object)`, `GetHashCode` | not listed | Always throw; nothing to gain | ReadOnlySpan.cs:188-202 |
| `ReadOnlySpan<T>.CastUp<TDerived>` | not listed | `TDerived : class`; never a non-copyable struct | ReadOnlySpan.cs:225 |
| explicit `IEnumerator<T>.Current` on the enumerators | not listed | Returns by value; reachable only through the interface | Span.cs:266 |

`Span<T>.Clear` and `Array.Clear` are listed. They overwrite elements with zero without disposing them. That drops values; it does not copy them.

### `Memory<T>` and `ReadOnlyMemory<T>`

`CL/Memory.cs`, `CL/ReadOnlyMemory.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| constructors, implicit conversions, `Empty`, `Length`, `IsEmpty`, `Slice` ×2 | listed | A view | Memory.cs:37-260 |
| `Span` | listed | Builds a span over the same storage; `MemoryManager<T>.GetSpan` gets no `T` | Memory.cs:263-350 |
| `Pin` | listed | Pins and returns a `MemoryHandle`; no element touched | Memory.cs:382-436 |
| `Equals(Memory<T>)`, `GetHashCode` | listed; `Equals` has `borrows` | Identity of the object, index and length | Memory.cs:467-484 |
| `ToString` | listed | Type name and length for `T` other than `char` | Memory.cs:208-215 |
| `CopyTo`, `TryCopyTo`, `ToArray` | not listed | Duplicate | Memory.cs:362-440 |
| `Equals(object)` | not listed | Not needed; it takes a boxed view | Memory.cs:447 |

### `ArraySegment<T>`

`CL/ArraySegment.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| constructors, `Empty`, `Array`, `Offset`, `Count`, `Slice` ×2, implicit conversion from `T[]` | listed | A view or its parts | :37-173 |
| `Item` | listed | Getter hands out `_array[i]`; setter stores | :73-93 |
| `GetEnumerator`, `Enumerator.MoveNext`, `Current`, `Dispose` | listed | `Current` hands out `_array[_current]` | :95, 281-333 |
| `Equals(ArraySegment<T>)`, `==`, `!=`, `GetHashCode` | listed; the first three have `borrows` | Compare or hash the array reference, offset and count | :101-171 |
| `CopyTo` ×3, `ToArray` | not listed | Duplicate | :104-155 |
| explicit `IList<T>`/`ICollection<T>` members (`IndexOf`, `Contains`, …) | not listed | Compare | :197-250 |

### `MemoryExtensions`

`CL/MemoryExtensions.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `AsSpan` ×10, `AsMemory` ×8 (arrays and `ArraySegment<T>`) | listed | `new Span<T>(…)` / `new Memory<T>(…)` | :26-70, 3695-3901 |
| `Overlaps` ×4 | listed, `borrows` | Compares addresses only | :4076-4108 |
| `Reverse<T>(Span<T>)` | listed, `borrows` | Swaps through a temporary: moves | :3683; SpanHelpers.cs:331-345 |
| `Contains`, `IndexOf*`, `LastIndexOf*`, `SequenceEqual`, `SequenceCompareTo`, `StartsWith`, `EndsWith`, `Sort`, `BinarySearch`, `Count`, `Replace`, `Split`, `Trim*` | not listed | Compare, or call user code by value | |
| `CopyTo<T>(T[], …)`, `ToArray`-style members | not listed | Duplicate | |

### `System.Array`

`CL/Array.cs`, `CLR/Array.CoreCLR.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `Length`, `LongLength`, `Rank` | listed | Read the header | Array.CoreCLR.cs:288-295 |
| `GetLength`, `GetLongLength`, `GetLowerBound`, `GetUpperBound` | listed | Read the header | Array.cs:764-1004 |
| `Empty<T>()` | listed | A shared empty array | Array.cs:1456 |
| `Clear(Array)`, `Clear(Array, int, int)` | listed, `borrows` | Zeroes: drops, no copy | Array.cs:659-700 |
| `Reverse<T>(T[])`, `Reverse<T>(T[], int, int)` | listed, `borrows` | Swaps in place | Array.cs:2275-2297 |
| `Reverse(Array)`, `Reverse(Array, int, int)` | not listed | Other element types go through `GetValue`/`SetValue`: boxed copies | Array.cs:2210-2272 |
| `Resize<T>` | not listed | Copies into a new array; another reference to the old array keeps the originals | Array.cs:42-67 |
| `Copy`, `ConstrainedCopy`, `Clone`, `CopyTo`, `Fill`, `IndexOf`, `LastIndexOf`, `Find*`, `Exists`, `TrueForAll`, `ForEach`, `Sort`, `BinarySearch`, `ConvertAll`, `AsReadOnly`, `GetValue`, `SetValue`, `GetEnumerator` | not listed | Copy, compare, call user code or box | |
| `Initialize` | not listed | Not needed | |

### `GC`, `RuntimeHelpers` and the `Nullable` Helper

| Member | Listed | Reason | Source |
|---|---|---|---|
| `GC.AllocateArray<T>(int, bool)` | listed, `new` | A new zeroed array | CLR/GC.CoreCLR.cs:841 |
| `GC.AllocateUninitializedArray<T>(int, bool)` | listed, `new` | A new array; contents are unspecified for a reference-free `T` (see below) | CLR/GC.CoreCLR.cs:798 |
| `RuntimeHelpers.IsReferenceOrContainsReferences<T>()` | listed | No value | CL/Runtime/CompilerServices/RuntimeHelpers.cs:175 |
| `RuntimeHelpers.GetSubArray<T>` | not listed | Copies a range | RuntimeHelpers.cs:26 |
| `RuntimeHelpers.CreateSpan<T>(RuntimeFieldHandle)` | not listed | Only for primitive RVA data; not applicable | |
| `Nullable.GetValueRefOrDefaultRef<T>` | listed | `ref readonly` to the inline value | CL/Nullable.cs:130 |

### `ArrayPool<T>` and `MemoryPool<T>`

`CL/Buffers/ArrayPool.cs`, `SharedArrayPool.cs`; `Lib/System.Memory/src/System/Buffers/MemoryPool.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `ArrayPool<T>.Shared`, `Create` ×2 | listed | Pool objects | ArrayPool.cs:39-60 |
| `ArrayPool<T>.Rent` | listed, `new` | Hands out an array taken from the pool or newly allocated (see below) | ArrayPool.cs:77; SharedArrayPool.cs:71-110 |
| `ArrayPool<T>.Return(T[], bool)` | listed | Stores the array; `Array.Clear` if asked | ArrayPool.cs:99; SharedArrayPool.cs:122-147 |
| `MemoryPool<T>.Shared`, `MaxBufferSize`, `Dispose` | listed | No value | MemoryPool.cs:18-39 |
| `MemoryPool<T>.Rent` | listed, `new` | Wraps a rented array in an owner | MemoryPool.cs:24 |
| `IMemoryOwner<T>.Memory` | listed | A view of the owned buffer | CL/Buffers/IMemoryOwner.cs |

- **`ArrayPool<T>.Rent` and `GC.AllocateUninitializedArray<T>` are listed as `new`.**
  - `SharedArrayPool<T>.Rent` hands out pooled arrays with whatever the last renter left (`CL/Buffers/SharedArrayPool.cs:71,89`). It allocates new arrays with `GC.AllocateUninitializedArray` (`SharedArrayPool.cs:110`), which does not zero a reference-free `T` (`CLR/GC.CoreCLR.cs:798`).
  - A by-value read of such an element is "new" and can be the bits of an abandoned value. This is treated as misuse of those APIs (the contents are unspecified until written), not as a copy.
- **`ArrayPool<T>.Return` has no marker.** It keeps the array: the pool hands it to the next `Rent`.

### `ReadOnlySequence<T>`, Its Segment and the Buffer Types

`ReadOnlySequence<T>` holds two objects and two integers, so it is a view (see [Views](#views)): every member hands out a view or a position.

| Member | What it does with a value | List entry |
|---|---|---|
| `T:System.Buffers.ReadOnlySequence`1` | a view over memory or segments; every member hands out a view or a position (ReadOnlySequence.cs) | view |
| `ReadOnlySequence<T>.Empty` | a static readonly field | not listed: a field cannot be an entry |
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

| Member | What it does with a value | List entry |
|---|---|---|
| `ReadOnlySequenceSegment<T>.Memory` | a segment and its memory | listed |
| `ReadOnlySequenceSegment<T>.Next` | a segment and its memory | listed |
| `ReadOnlySequenceSegment<T>.RunningIndex` | a segment and its memory | listed |

`IBufferWriter<T>` hands out memory of the writer to write into, which is a view. An entry for a member of an interface is a statement about every implementation of it.

| Member | What it does with a value | List entry |
|---|---|---|
| `IBufferWriter<T>.Advance(int)` | hands out memory of the writer to write into (IBufferWriter.cs) | listed |
| `IBufferWriter<T>.GetMemory(int)` | hands out memory of the writer to write into (IBufferWriter.cs) | listed |
| `IBufferWriter<T>.GetSpan(int)` | hands out memory of the writer to write into (IBufferWriter.cs) | listed |

`ArrayBufferWriter<T>` grows by moving what was written into a larger array (`Array.Resize`).

| Member | What it does with a value | List entry |
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

`BuffersExtensions` copies, compares or writes elements.

| Member | What it does with a value | List entry |
|---|---|---|
| `BuffersExtensions.CopyTo<T>(in ReadOnlySequence<T>, Span<T>)` | copies elements | not listed: copies |
| `BuffersExtensions.PositionOf<T>(in ReadOnlySequence<T>, T)` | compares values with Equals | not listed: passes a value to Equals |
| `BuffersExtensions.ToArray<T>(in ReadOnlySequence<T>)` | copies elements | not listed: copies |
| `BuffersExtensions.Write<T>(IBufferWriter<T>, ReadOnlySpan<T>)` | copies elements | not listed: copies |

`MemoryManager<T>` hands out the memory that it manages, and pins it with a handle of its own at every call.

| Member | What it does with a value | List entry |
|---|---|---|
| `MemoryManager<T>.GetSpan()` | the memory that it manages (MemoryManager.cs) | listed |
| `MemoryManager<T>.Pin(int)` | pins and hands out a handle of its own at every call (MemoryManager.cs) | new |
| `MemoryManager<T>.Unpin()` | the memory that it manages (MemoryManager.cs) | listed |
| `MemoryManager<T>.Memory` | the memory that it manages (MemoryManager.cs) | listed |

## Marshal and Unsafe Helpers

Members of `Unsafe`, `MemoryMarshal`, `CollectionsMarshal`, `ImmutableCollectionsMarshal` and `Marshal` are listed for one fact only: they make no copy.

### Listing `Unsafe` and the Marshal Classes

- **Listed:** members that only compute or compare references, or that make a typed reference with no copy: `Add`, `Subtract`, `AreSame`, `AsRef(ref)`, `AsRef(void*)`, `NullRef`, `GetReference`, `CreateSpan`.
- **Not listed:** members that copy (`Read*`, `Write*`, `Copy`, `BitCast`), that reinterpret to another type (`As<TFrom, TTo>`, `MemoryMarshal.Cast`/`AsBytes`/`AsRef<T>(Span<byte>)`), or that give out a pointer (`AsPointer`, `Marshal.UnsafeAddrOfPinnedArrayElement`). Those lose the type, as a conversion to a type that hides it does (see [Containers and Conversions](../../Docs/articles/rules/conversions.md)).
- **`AsRef<T>(void*)` is listed.** It creates a typed reference over untyped memory and copies nothing, but it is the one that lets native memory hold a guard.

### `MemoryMarshal`

`CL/Runtime/InteropServices/MemoryMarshal.cs`, `CLR/Runtime/InteropServices/MemoryMarshal.CoreCLR.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `GetReference` ×2, `GetArrayDataReference<T>(T[])` | listed | `ref` to the first element | :80-86; CoreCLR:22 |
| `CreateSpan`, `CreateReadOnlySpan` | listed | A view over a reference | :226, 243 |
| `AsMemory<T>(ReadOnlyMemory<T>)` | listed | A view | :73 |
| `TryGetArray`, `TryGetMemoryManager` ×2 | listed | Hand out the underlying storage | :270-353 |
| `CreateFromPinnedArray` | listed | A view | :601 |
| `GetArrayDataReference(Array)` | not listed | `ref byte`: the type is lost | CoreCLR:38 |
| `AsBytes` ×2, `Cast` ×2, `AsRef<T>(Span<byte>)` ×2 | not listed | Reinterpret as another type | :30-169, 549-571 |
| `Read`, `TryRead`, `Write`, `TryWrite` | not listed | Copy through bytes | :468-526 |
| `ToEnumerable` | not listed | Yields copies by value from an `IEnumerable<T>` that can be enumerated again; not needed | :379 |

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `MemoryMarshal.AsBytes<T>(ReadOnlySpan<T>)` | reinterprets the memory | not listed: erases the type |
| `MemoryMarshal.AsBytes<T>(Span<T>)` | reinterprets the memory | not listed: erases the type |
| `MemoryMarshal.AsMemory<T>(ReadOnlyMemory<T>)` | a basic member: see the summary table | listed |
| `MemoryMarshal.AsRef<T>(ReadOnlySpan<byte>)` | reinterprets the memory | not listed: erases the type |
| `MemoryMarshal.AsRef<T>(Span<byte>)` | reinterprets the memory | not listed: erases the type |
| `MemoryMarshal.Cast<TFrom, TTo>(ReadOnlySpan<TFrom>)` | reinterprets the memory | not listed: erases the type |
| `MemoryMarshal.Cast<TFrom, TTo>(Span<TFrom>)` | reinterprets the memory | not listed: erases the type |
| `MemoryMarshal.CreateFromPinnedArray<T>(T[]?, int, int)` | a basic member: see the summary table | listed |
| `MemoryMarshal.CreateReadOnlySpanFromNullTerminated(byte*)` | not generic | not listed: not generic |
| `MemoryMarshal.CreateReadOnlySpanFromNullTerminated(char*)` | not generic | not listed: not generic |
| `MemoryMarshal.CreateReadOnlySpan<T>(scoped ref readonly T, int)` | a basic member: see the summary table | listed |
| `MemoryMarshal.CreateSpan<T>(scoped ref T, int)` | a basic member: see the summary table | listed |
| `MemoryMarshal.GetArrayDataReference(Array)` | not generic | not listed: not generic |
| `MemoryMarshal.GetArrayDataReference<T>(T[])` | a basic member: see the summary table | listed |
| `MemoryMarshal.GetReference<T>(ReadOnlySpan<T>)` | a basic member: see the summary table | listed |
| `MemoryMarshal.GetReference<T>(Span<T>)` | a basic member: see the summary table | listed |
| `MemoryMarshal.Read<T>(ReadOnlySpan<byte>)` | copies a value to or from bytes | not listed: copies |
| `MemoryMarshal.ToEnumerable<T>(ReadOnlyMemory<T>)` | turns a view into a sequence of by-value reads | not listed: copies |
| `MemoryMarshal.TryGetArray<T>(ReadOnlyMemory<T>, out ArraySegment<T>)` | a basic member: see the summary table | listed |
| `MemoryMarshal.TryGetMemoryManager<T, TManager>(ReadOnlyMemory<T>, out TManager?)` | a basic member: see the summary table | listed |
| `MemoryMarshal.TryGetMemoryManager<T, TManager>(ReadOnlyMemory<T>, out TManager?, out int, out int)` | a basic member: see the summary table | listed |
| `MemoryMarshal.TryGetString(ReadOnlyMemory<char>, out string?, out int, out int)` | not generic | not listed: not generic |
| `MemoryMarshal.TryRead<T>(ReadOnlySpan<byte>, out T)` | copies a value to or from bytes | not listed: copies |
| `MemoryMarshal.TryWrite<T>(Span<byte>, in T)` | copies a value to or from bytes | not listed: copies |
| `MemoryMarshal.Write<T>(Span<byte>, in T)` | copies a value to or from bytes | not listed: copies |

### `CollectionsMarshal`

`CL/Runtime/InteropServices/CollectionsMarshal.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `AsSpan<T>(List<T>)` | listed | A span over `_items` | :23 |
| `GetValueRefOrNullRef` ×2, `GetValueRefOrAddDefault` ×2 | listed | `ref` into the entry; limited with `for=TValue` (see [`Dictionary<TKey, TValue>`](#dictionarytkey-tvalue)) | :78-123 |
| `SetCount<T>` | not listed | Re-exposes stale slots (see below) | :140-159 |

**`CollectionsMarshal.SetCount` is not listed, because of stale slots.**

- For a reference-free `T`, `List<T>.RemoveAt` and `Clear` do not clear the vacated slots (`CL/Collections/Generic/List.cs:1001-1019`, `320-340`).
- `RemoveAt` shifts elements down, so the slot past the end keeps a bitwise duplicate of the last element. `SetCount` with a larger count makes that slot visible again (`CL/Runtime/InteropServices/CollectionsMarshal.cs:140-159`): two live instances.
- `Stack<T>.Pop` and `Queue<T>.Dequeue` leave the same kind of stale slot. Nothing on the list can reach it, which is why they keep `new`.

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `CollectionsMarshal.AsBytes(BitArray?)` | not generic | not listed: not generic |
| `CollectionsMarshal.AsSpan<T>(List<T>?)` | a basic member: see the summary table | listed |
| `CollectionsMarshal.GetValueRefOrNullRef<TKey, TValue>(Dictionary<TKey, TValue>, TKey)` | a basic member: see the summary table | for=TValue |
| `CollectionsMarshal.GetValueRefOrNullRef<TKey, TValue, TAlternateKey>(Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>, TAlternateKey)` | a basic member: see the summary table | for=TValue |
| `CollectionsMarshal.GetValueRefOrAddDefault<TKey, TValue>(Dictionary<TKey, TValue>, TKey, out bool)` | a basic member: see the summary table | for=TValue |
| `CollectionsMarshal.GetValueRefOrAddDefault<TKey, TValue, TAlternateKey>(Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>, TAlternateKey, out bool)` | a basic member: see the summary table | for=TValue |
| `CollectionsMarshal.SetCount<T>(List<T>, int)` | grows the count over what is past it, which for a T with no reference still holds what was removed or cleared (CollectionsMarshal.cs 140-159) | not listed: makes an element again of what was removed |

### Other Marshal Classes

| Member | Listed | Reason | Source |
|---|---|---|---|
| `ImmutableCollectionsMarshal.AsImmutableArray`, `AsArray`, `AsMemory(Builder)` | listed | Same storage, no copy | Lib/System.Collections.Immutable/src/System/Runtime.InteropServices/ImmutableCollectionsMarshal.cs:32-68 |
| `SequenceMarshal.TryGetReadOnlySequenceSegment`, `TryGetArray`, `TryGetReadOnlyMemory` | listed | Hand out the underlying storage | Lib/System.Memory/src/System/Runtime/InteropServices/SequenceMarshal.cs:18-50 |
| `SequenceMarshal.TryRead<T>` | not listed | `T : unmanaged`; copies from bytes | :73 |
| `Marshal.SizeOf<T>()`, `Marshal.OffsetOf<T>(string)` | listed | No value | CL/Runtime/InteropServices/Marshal.cs:122, 182 |
| `Marshal.SizeOf<T>(T)`, `StructureToPtr<T>`, `PtrToStructure<T>` ×2, `GetNativeVariantForObject<T>`, `GetObjectForNativeVariant<T>` | not listed | Copy the value | Marshal.cs:97, 545, 594, 604 |
| `Marshal.UnsafeAddrOfPinnedArrayElement<T>` | not listed | Hands out a pointer: the type is lost | Marshal.cs:173 |
| `Marshal.DestroyStructure<T>` | not listed | Not applicable | Marshal.cs:627 |
| `NativeMemory` | not listed | Has no generic members | |

### `Unsafe`

`CL/Runtime/CompilerServices/Unsafe.cs`

| Member | Listed | Reason | Source |
|---|---|---|---|
| `SizeOf` | listed | No value | :48 |
| `Add` ×4, `AddByteOffset` ×2, `Subtract` ×4, `SubtractByteOffset` ×2 | listed | Reference or pointer arithmetic | :100-210, 629, 784-907 |
| `AreSame`, `IsAddressGreaterThan`, `IsAddressLessThan`, `ByteOffset` | listed | Compare addresses | :235, 396, 420, 708 |
| `AsRef<T>(ref readonly T)`, `AsRef<T>(void*)` | listed | A typed reference, no copy (see above) | :674, 690 |
| `NullRef`, `IsNullRef` | listed | No value | :727, 748 |
| `AsPointer` | not listed | Hands out a pointer: the type is lost | :29 |
| `As<TFrom, TTo>(ref)`, `As<T>(object)` | not listed | Reinterpret | :64, 81 |
| `BitCast`, `Copy` ×2, `Read`, `Write`, `ReadUnaligned` ×2, `WriteUnaligned` ×2 | not listed | Copy | :254-661 |
| `SkipInit` | not listed | Hands out an uninitialized value that can hold stale stack bits | :769 |
| `Unbox` | listed | Returns a `ref` into the box: the value is used in place | :930 |

`Unsafe.Unbox<T>` is listed because it returns a reference into the box.

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `Unsafe.AddByteOffset<T>(ref T, nint)` | a basic member: see the summary table | listed |
| `Unsafe.AddByteOffset<T>(ref T, nuint)` | a basic member: see the summary table | listed |
| `Unsafe.Add<T>(void*, int)` | a basic member: see the summary table | listed |
| `Unsafe.Add<T>(ref T, int)` | a basic member: see the summary table | listed |
| `Unsafe.Add<T>(ref T, nint)` | a basic member: see the summary table | listed |
| `Unsafe.Add<T>(ref T, nuint)` | a basic member: see the summary table | listed |
| `Unsafe.AreSame<T>(ref readonly T, ref readonly T)` | a basic member: see the summary table | listed |
| `Unsafe.AsPointer<T>(ref readonly T)` | reinterprets the value or the reference | not listed: erases the type |
| `Unsafe.AsRef<T>(void*)` | a basic member: see the summary table | listed |
| `Unsafe.AsRef<T>(scoped ref readonly T)` | a basic member: see the summary table | listed |
| `Unsafe.As<T>(object?)` | reinterprets the value or the reference | not listed: erases the type |
| `Unsafe.As<TFrom, TTo>(ref TFrom)` | reinterprets the value or the reference | not listed: erases the type |
| `Unsafe.BitCast<TFrom, TTo>(TFrom)` | reinterprets the value or the reference | not listed: erases the type |
| `Unsafe.ByteOffset<T>(ref readonly T, ref readonly T)` | a basic member: see the summary table | listed |
| `Unsafe.CopyBlock(ref byte, ref readonly byte, uint)` | copies a value | not listed: copies |
| `Unsafe.CopyBlock(void*, void*, uint)` | copies a value | not listed: copies |
| `Unsafe.CopyBlockUnaligned(ref byte, ref readonly byte, uint)` | copies a value | not listed: copies |
| `Unsafe.CopyBlockUnaligned(void*, void*, uint)` | copies a value | not listed: copies |
| `Unsafe.Copy<T>(void*, ref readonly T)` | copies a value | not listed: copies |
| `Unsafe.Copy<T>(ref T, void*)` | copies a value | not listed: copies |
| `Unsafe.InitBlock(ref byte, byte, uint)` | writes bytes over memory | not listed: makes a value that no constructor made |
| `Unsafe.InitBlock(void*, byte, uint)` | writes bytes over memory | not listed: makes a value that no constructor made |
| `Unsafe.InitBlockUnaligned(ref byte, byte, uint)` | writes bytes over memory | not listed: makes a value that no constructor made |
| `Unsafe.InitBlockUnaligned(void*, byte, uint)` | writes bytes over memory | not listed: makes a value that no constructor made |
| `Unsafe.IsAddressGreaterThan<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsAddressGreaterThanOrEqualTo<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsAddressLessThan<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsAddressLessThanOrEqualTo<T>(ref readonly T, ref readonly T)` | compares two addresses | listed |
| `Unsafe.IsNullRef<T>(ref readonly T)` | a basic member: see the summary table | listed |
| `Unsafe.NullRef<T>()` | a basic member: see the summary table | listed |
| `Unsafe.ReadUnaligned<T>(scoped ref readonly byte)` | copies a value | not listed: copies |
| `Unsafe.ReadUnaligned<T>(void*)` | copies a value | not listed: copies |
| `Unsafe.Read<T>(void*)` | copies a value | not listed: copies |
| `Unsafe.SizeOf<T>()` | a basic member: see the summary table | listed |
| `Unsafe.SkipInit<T>(out T)` | leaves a value that no constructor made | not listed: not a member that low-level code needs listed |
| `Unsafe.SubtractByteOffset<T>(ref T, nint)` | a basic member: see the summary table | listed |
| `Unsafe.SubtractByteOffset<T>(ref T, nuint)` | a basic member: see the summary table | listed |
| `Unsafe.Subtract<T>(void*, int)` | a basic member: see the summary table | listed |
| `Unsafe.Subtract<T>(ref T, int)` | a basic member: see the summary table | listed |
| `Unsafe.Subtract<T>(ref T, nint)` | a basic member: see the summary table | listed |
| `Unsafe.Subtract<T>(ref T, nuint)` | a basic member: see the summary table | listed |
| `Unsafe.Unbox<T>(object)` | a basic member: see the summary table | listed |
| `Unsafe.WriteUnaligned<T>(ref byte, T)` | copies a value | not listed: copies |
| `Unsafe.WriteUnaligned<T>(void*, T)` | copies a value | not listed: copies |
| `Unsafe.Write<T>(void*, T)` | copies a value | not listed: copies |

### Members That Make Read-only Storage Writable

Five listed members turn something read-only into something writable: `Unsafe.AsRef<T>(in T)`, `MemoryMarshal.GetReference<T>(ReadOnlySpan<T>)`, `MemoryMarshal.AsMemory<T>(ReadOnlyMemory<T>)`, `MemoryMarshal.TryGetArray<T>(ReadOnlyMemory<T>, out ArraySegment<T>)` and `ImmutableCollectionsMarshal.AsArray<T>(ImmutableArray<T>)`. Several rules of the analyzer take `readonly` at its word, and a write through one of these breaks them: a test program with a guard whose `readonly` `Dispose` writes through `Unsafe.AsRef(in this)`, called as `maybe?.Dispose()`, builds with no diagnostic and releases one acquisition twice.

They are listed all the same, on this principle: **the list says of a member only that it makes no copy, and the analyzer guarantees nothing about code that uses the unsafe classes to do what the language otherwise rules out.** `Unsafe.AsRef(in)` can change what must not change, and `Unsafe.NullRef<T>()` can hand a null reference to code that does not expect one; neither is the analyzer's to guard against. The criterion in [How the Criterion Is Applied](#how-the-criterion-is-applied) stands as it is, with no second criterion. [What Is Not Covered](../../Docs/articles/rules/not-covered.md#unsafe-classes) says so.

## Atomic Helpers

`Interlocked` and `Volatile` entries can only apply to containers: a non-primitive struct is rejected at run time.

| Member | Listed | Reason | Source |
|---|---|---|---|
| `Interlocked.Exchange<T>(ref T, T)` | listed, `new` | .NET 9+: no constraint, but any struct that is not primitive or enum throws `NotSupportedException`. .NET 8: `where T : class`. So it only ever applies to containers, and it takes the reference out of the location | CL/Threading/Interlocked.cs:243-283 |
| `Interlocked.CompareExchange<T>(ref T, T, T)` | listed | Same constraint situation; reference comparison, no user code; the returned value may still be in the location, hence no `new` | Interlocked.cs:495 |
| `Volatile.Read<T>`, `Volatile.Write<T>` | listed | `where T : class?` in every version: containers only | CL/Threading/Volatile.cs:221, 226 |

## Collections

The collections are covered at a basic level: construct, add, remove, count, clear, indexer and enumeration. The tables headed "every public member" go through each public member of the type, from the .NET 10 reference assemblies, with the entry that the list has for it (marker, `for=`) or the reason that it is not listed. They exist so that the first hour with the analyzer is not spent on list entries for the framework: the types are what a developer of low-level libraries reaches for with a non-copyable type. The rule for a listed member: it makes no copy of a value other than the one it hands out, passes no value to a comparer, `Equals`, `GetHashCode`, `ToString` or a delegate, and does not erase the type. `LinkedList<T>`, `ConcurrentQueue<T>`, `ConcurrentStack<T>` and `ConcurrentDictionary<TKey, TValue>` have entries of their own: the first below, the others in [Channels and Concurrent Collections](#channels-and-concurrent-collections).

### Basic Members

| Type | Listed | Not listed (main reasons) | Source |
|---|---|---|---|
| `List<T>` | `#ctor`, `#ctor(int)`, `Add`, `Insert`, `RemoveAt`, `RemoveRange`, `Reverse` ×2, `Clear`, `Count`, `Capacity` (setter moves to a new private array), `Item`, `EnsureCapacity`, `TrimExcess`, `GetEnumerator`, `Enumerator.MoveNext`/`Current`/`Dispose` (copy into `_current`; see [How the Criterion Is Applied](#how-the-criterion-is-applied)) | `#ctor(IEnumerable<T>)`, `AddRange`, `InsertRange`, `GetRange`, `Slice`, `CopyTo` ×3, `ToArray`, `ConvertAll`, `AsReadOnly` (duplicate); `Contains`, `IndexOf`, `LastIndexOf`, `Remove(T)`, `BinarySearch`, `Sort` ×4 (compare); `Find*`, `Exists`, `ForEach`, `TrueForAll`, `RemoveAll` (delegate by value) | CL/Collections/Generic/List.cs:37-1229 |
| `Stack<T>` | `#ctor` ×2, `Push`, `Pop` new, `TryPop` new, `Peek`, `TryPeek`, `Count`, `Clear`, enumerator ×3, `EnsureCapacity`, `TrimExcess` ×2, `Capacity` | `#ctor(IEnumerable<T>)`, `CopyTo`, `ToArray` (duplicate); `Contains` (compare) | Lib/System.Collections/src/System/Collections/Generic/Stack.cs:35-391; `Pop` at :221-243 |
| `Queue<T>` | `#ctor` ×2, `Enqueue`, `Dequeue` new, `TryDequeue` new, `Peek`, `TryPeek`, `Count`, `Clear`, enumerator ×3, `EnsureCapacity`, `TrimExcess` ×2, `Capacity` | `#ctor(IEnumerable<T>)`, `CopyTo`, `ToArray` (duplicate); `Contains` (compare) | CL/Collections/Generic/Queue.cs:32-482; `Dequeue` at :194-237 |
| `Dictionary<TKey, TValue>` | `#ctor` ×4, `Add`, `TryAdd`, `Remove(TKey)`, `Remove(TKey, out TValue)` new (moves the value out and clears the slot, :1405-1417), `TryGetValue`, `Item`, `ContainsKey`, `Count`, `Clear`, enumerator ×3, `Keys` with its collection and enumerator, `Comparer`, `Capacity`, `EnsureCapacity`, `TrimExcess` ×2, the alternate lookup; all with `for=TValue` | `ContainsValue` (compare); constructors from a collection, `CopyTo`, `Values` (copy); `GetObjectData`, `OnDeserialization` (copy); explicit `ICollection<KeyValuePair>` members (compare) | CL/Collections/Generic/Dictionary.cs:40-1811 |

The entries of the other collections are in the sections below. `List<T>.RemoveRange` and `Reverse` shift or swap elements in place, so they are listed.

### `List<T>`

| Member | What it does with a value | List entry |
|---|---|---|
| `List<T>.List()` | a basic member: see the summary table | listed |
| `List<T>.List(IEnumerable<T>)` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.List(int)` | a basic member: see the summary table | listed |
| `List<T>.Add(T)` | a basic member: see the summary table | listed |
| `List<T>.AddRange(IEnumerable<T>)` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.AsReadOnly()` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.BinarySearch(int, int, T, IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.BinarySearch(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.BinarySearch(T, IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.Clear()` | a basic member: see the summary table | listed |
| `List<T>.Contains(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.ConvertAll<TOutput>(Converter<T, TOutput>)` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.CopyTo(int, T[], int, int)` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.CopyTo(T[])` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.CopyTo(T[], int)` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.EnsureCapacity(int)` | a basic member: see the summary table | listed |
| `List<T>.Exists(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.Find(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindAll(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindIndex(int, int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindIndex(int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindIndex(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindLast(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindLastIndex(int, int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindLastIndex(int, Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.FindLastIndex(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.ForEach(Action<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.GetEnumerator()` | a basic member: see the summary table | listed |
| `List<T>.GetRange(int, int)` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.IndexOf(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.IndexOf(T, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.IndexOf(T, int, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.Insert(int, T)` | a basic member: see the summary table | listed |
| `List<T>.InsertRange(int, IEnumerable<T>)` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.LastIndexOf(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.LastIndexOf(T, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.LastIndexOf(T, int, int)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.Remove(T)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.RemoveAll(Predicate<T>)` | passes each element to a delegate | not listed: passes a value to a delegate |
| `List<T>.RemoveAt(int)` | a basic member: see the summary table | listed |
| `List<T>.RemoveRange(int, int)` | shifts or swaps elements in place (List.cs 1020-1080) | listed |
| `List<T>.Reverse()` | shifts or swaps elements in place (List.cs 1020-1080) | listed |
| `List<T>.Reverse(int, int)` | shifts or swaps elements in place (List.cs 1020-1080) | listed |
| `List<T>.Slice(int, int)` | copies a range (GetRange) | not listed: copies |
| `List<T>.Sort()` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.Sort(IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.Sort(Comparison<T>)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.Sort(int, int, IComparer<T>?)` | compares elements with a comparer or Equals (List.cs 289-330, 660-720, 1000-1015, 1110-1170) | not listed: passes a value to a comparer |
| `List<T>.ToArray()` | copies elements, or wraps the list as an interface that reads by value | not listed: copies |
| `List<T>.TrimExcess()` | a basic member: see the summary table | listed |
| `List<T>.TrueForAll(Predicate<T>)` | passes each element by value to a delegate (List.cs 440-640, 1210) | not listed: passes a value to a delegate |
| `List<T>.Capacity` | a basic member: see the summary table | listed |
| `List<T>.Count` | a basic member: see the summary table | listed |
| `List<T>.this[int]` | a basic member: see the summary table | listed |
| `List<T>.Enumerator.Dispose()` | a basic member: see the summary table | listed |
| `List<T>.Enumerator.MoveNext()` | a basic member: see the summary table | listed |
| `List<T>.Enumerator.Current` | a basic member: see the summary table | listed |

### `Stack<T>` and `Queue<T>`

`TrimExcess`, `EnsureCapacity` and `Capacity` of both types move the elements into a new array, which is a move.

`Queue<T>`:

| Member | What it does with a value | List entry |
|---|---|---|
| `Queue<T>.Queue()` | a basic member: see the summary table | listed |
| `Queue<T>.Queue(IEnumerable<T>)` | copies the source | not listed: copies |
| `Queue<T>.Queue(int)` | a basic member: see the summary table | listed |
| `Queue<T>.Clear()` | a basic member: see the summary table | listed |
| `Queue<T>.Contains(T)` | compares values with EqualityComparer<T>.Default | not listed: passes a value to Equals |
| `Queue<T>.CopyTo(T[], int)` | copies every element | not listed: copies |
| `Queue<T>.Dequeue()` | a basic member: see the summary table | new |
| `Queue<T>.Enqueue(T)` | a basic member: see the summary table | listed |
| `Queue<T>.GetEnumerator()` | a basic member: see the summary table | listed |
| `Queue<T>.Peek()` | a basic member: see the summary table | listed |
| `Queue<T>.ToArray()` | copies every element | not listed: copies |
| `Queue<T>.TrimExcess()` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.TrimExcess(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.EnsureCapacity(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.TryDequeue(out T)` | a basic member: see the summary table | new |
| `Queue<T>.TryPeek(out T)` | a basic member: see the summary table | listed |
| `Queue<T>.Count` | a basic member: see the summary table | listed |
| `Queue<T>.Capacity` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Queue<T>.Enumerator.Dispose()` | a basic member: see the summary table | listed |
| `Queue<T>.Enumerator.MoveNext()` | a basic member: see the summary table | listed |
| `Queue<T>.Enumerator.Current` | a basic member: see the summary table | listed |

`Stack<T>`:

| Member | What it does with a value | List entry |
|---|---|---|
| `Stack<T>.Stack()` | a basic member: see the summary table | listed |
| `Stack<T>.Stack(IEnumerable<T>)` | copies the source | not listed: copies |
| `Stack<T>.Stack(int)` | a basic member: see the summary table | listed |
| `Stack<T>.Clear()` | a basic member: see the summary table | listed |
| `Stack<T>.Contains(T)` | compares values with EqualityComparer<T>.Default | not listed: passes a value to Equals |
| `Stack<T>.CopyTo(T[], int)` | copies every element | not listed: copies |
| `Stack<T>.GetEnumerator()` | a basic member: see the summary table | listed |
| `Stack<T>.Peek()` | a basic member: see the summary table | listed |
| `Stack<T>.Pop()` | a basic member: see the summary table | new |
| `Stack<T>.Push(T)` | a basic member: see the summary table | listed |
| `Stack<T>.EnsureCapacity(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.ToArray()` | copies every element | not listed: copies |
| `Stack<T>.TrimExcess()` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.TrimExcess(int)` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.TryPeek(out T)` | a basic member: see the summary table | listed |
| `Stack<T>.TryPop(out T)` | a basic member: see the summary table | new |
| `Stack<T>.Count` | a basic member: see the summary table | listed |
| `Stack<T>.Capacity` | moves the elements into a new array (Queue.cs 310-393, Stack.cs 165-327) | listed |
| `Stack<T>.Enumerator.Dispose()` | a basic member: see the summary table | listed |
| `Stack<T>.Enumerator.MoveNext()` | a basic member: see the summary table | listed |
| `Stack<T>.Enumerator.Current` | a basic member: see the summary table | listed |

### `Dictionary<TKey, TValue>`

**The entries are limited to `TValue`.**

- An entry covers every type parameter of the member. `Add`, `TryAdd`, `Remove`, `TryGetValue`, the indexer and `ContainsKey` pass the key by value to `IEqualityComparer<TKey>`, and so do the `CollectionsMarshal` dictionary entries.
- With a non-copyable `TKey` these entries would let copies through. They are judged for a non-copyable `TValue`, and the list limits them with `;for=TValue`, so a non-copyable `TKey` gets a warning.
- The `for=TValue` limit applies to the basic entries of `Dictionary<TKey, TValue>` and its enumerator, `IDictionary<TKey, TValue>`, `IReadOnlyDictionary<TKey, TValue>` and the `CollectionsMarshal.GetValueRef...` methods (27 entries), and to the dictionary types that the tables below cover.

**The keys.** `Dictionary<,>.Keys` hands out the key collection, which holds the dictionary. The key collection and its enumerator read keys only, so no value is copied.

| Member | What it does with a value | Marker |
|---|---|---|
| `Dictionary<,>.Keys` | hands out the key collection, which holds the dictionary | none |
| `KeyCollection.GetEnumerator()` | makes an enumerator that holds the dictionary, an index and a key, and no value (`Dictionary.cs` 1900, 2008-2024), so the enumerator is a new one whatever the collection is | `new` |
| `KeyCollection.Count`, `.Contains(TKey)`, `.CopyTo(TKey[], int)` | read keys and the count only (`Dictionary.cs` 1900-1937) | none |
| `KeyCollection.Enumerator.MoveNext()`, `.Current`, `.Dispose()` | `_currentKey = entry.key`: no value is read | none |
| `KeyCollection(Dictionary<,>)` | makes a key collection | **not listed**: the property is how one is got |

**The values.** `Dictionary<,>.Values` hands out the value collection, which holds the dictionary (`Dictionary.cs` 222, 2081-2093). Its enumerator copies each value into `_currentValue`, which `Current` hands out, and reads no key (`Dictionary.cs` 2206-2244): that is the copy inside what is handed out of rule 2, as the enumerator of `List<T>` makes. So a `foreach` over the values of a stored dictionary gets one warning, for the copy that `Current` hands out.

| Member | What it does with a value | Marker |
|---|---|---|
| `Dictionary<,>.Values` | hands out the value collection, which holds the dictionary | none |
| `ValueCollection.GetEnumerator()`, `.Count` | make an enumerator that holds the dictionary; read the count | none |
| `ValueCollection.Enumerator.MoveNext()`, `.Current`, `.Dispose()` | `_currentValue = entry.value`, which `Current` hands out | none |
| `ValueCollection.CopyTo(TValue[], int)` | copies every value into an array | **not listed**: copies |

**The key and value collections are erasable for the other type.** `KeyCollection` is marked `erasable;for=TValue` and `ValueCollection` `erasable;for=TKey`, so `map.Keys.ToList()` converts the key collection to `IEnumerable<TKey>` with no warning. See [Key and Value Collections That Are Erasable for the Other Type](#key-and-value-collections-that-are-erasable-for-the-other-type).

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `Dictionary<TKey, TValue>.Dictionary()` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Dictionary(IDictionary<TKey, TValue>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `Dictionary<TKey, TValue>.Dictionary(IDictionary<TKey, TValue>, IEqualityComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `Dictionary<TKey, TValue>.Dictionary(IEnumerable<KeyValuePair<TKey, TValue>>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `Dictionary<TKey, TValue>.Dictionary(IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `Dictionary<TKey, TValue>.Dictionary(IEqualityComparer<TKey>?)` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Dictionary(int)` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Dictionary(int, IEqualityComparer<TKey>?)` | makes an empty dictionary; a comparer is given keys only (Dictionary.cs 40-84) | for=TValue |
| `Dictionary<TKey, TValue>.Add(TKey, TValue)` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Clear()` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.ContainsKey(TKey)` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.ContainsValue(TValue)` | EqualityComparer<TValue>.Default.Equals on every value (Dictionary.cs 300-368) | not listed: passes a value to Equals |
| `Dictionary<TKey, TValue>.EnsureCapacity(int)` | moves the entries into a new array, the old one is dropped (Resize 1248-1290, CopyEntries 1600-1620) | for=TValue |
| `Dictionary<TKey, TValue>.GetAlternateLookup<TAlternateKey>()` | hands out a lookup that holds the dictionary (Dictionary.cs 650-685) | for=TValue |
| `Dictionary<TKey, TValue>.GetEnumerator()` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.GetObjectData(SerializationInfo, StreamingContext)` | serializes or restores every value | not listed: copies |
| `Dictionary<TKey, TValue>.OnDeserialization(object?)` | serializes or restores every value | not listed: copies |
| `Dictionary<TKey, TValue>.Remove(TKey)` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Remove(TKey, out TValue)` | a basic member: see the summary table | new;for=TValue |
| `Dictionary<TKey, TValue>.TrimExcess()` | moves the entries into a new array, the old one is dropped (Resize 1248-1290, CopyEntries 1600-1620) | for=TValue |
| `Dictionary<TKey, TValue>.TrimExcess(int)` | moves the entries into a new array, the old one is dropped (Resize 1248-1290, CopyEntries 1600-1620) | for=TValue |
| `Dictionary<TKey, TValue>.TryAdd(TKey, TValue)` | a basic member: see the summary table; takes the value only where it returns true (see [Members That Keep Nothing of a Value in Some Outcomes](#members-that-keep-nothing-of-a-value-in-some-outcomes)) | borrowswhen=false;for=TValue |
| `Dictionary<TKey, TValue>.TryGetAlternateLookup<TAlternateKey>(out Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>)` | hands out a lookup that holds the dictionary (Dictionary.cs 650-685) | for=TValue |
| `Dictionary<TKey, TValue>.TryGetValue(TKey, out TValue)` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Comparer` | the comparer of keys, or the length of the entries array (Dictionary.cs 160-185) | for=TValue |
| `Dictionary<TKey, TValue>.Count` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Capacity` | the comparer of keys, or the length of the entries array (Dictionary.cs 160-185) | for=TValue |
| `Dictionary<TKey, TValue>.this[TKey]` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Keys` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Values` | a basic member: the value collection, which holds the dictionary (Dictionary.cs 222) | for=TValue |
| `T:System.Collections.Generic.Dictionary`2.AlternateLookup`1` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | view |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.ContainsKey(TAlternateKey)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryAdd(TAlternateKey, TValue)` | stores into a slot found by the alternate key (Dictionary.cs 712-750, 947-960), only where it returns true | borrowswhen=false;for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TKey, out TValue)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Remove(TAlternateKey)` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Remove(TAlternateKey, out TKey, out TValue)` | takes the entry out (Dictionary.cs 872-940) | new;for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Dictionary` | looks up by the alternate key; reads by value what the dictionary holds (Dictionary.cs 751-870) | for=TValue |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.this[TAlternateKey]` | stores into a slot found by the alternate key (Dictionary.cs 712-750, 947-960) | for=TValue |
| `Dictionary<TKey, TValue>.Enumerator.Dispose()` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Enumerator.MoveNext()` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.Enumerator.Current` | a basic member: see the summary table | for=TValue |
| `T:System.Collections.Generic.Dictionary`2.KeyCollection` | the type: its generic interfaces hand out keys, its code reads keys only (Dictionary.cs 1886-2079), and `ICollection.SyncRoot` hands out the dictionary's lock object, which is the dictionary itself on .NET: see [Key and Value Collections That Are Erasable for the Other Type](#key-and-value-collections-that-are-erasable-for-the-other-type) | erasable;for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.KeyCollection(Dictionary<TKey, TValue>)` | a view of the keys; judged with Keys | not listed: a constructor of a view is not what code calls |
| `Dictionary<TKey, TValue>.KeyCollection.CopyTo(TKey[], int)` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.GetEnumerator()` | makes an enumerator that holds no value (Dictionary.cs 1900, 2008-2024) | new;for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Contains(TKey)` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Count` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Enumerator.Dispose()` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Enumerator.MoveNext()` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.KeyCollection.Enumerator.Current` | a basic member: see the summary table | for=TValue |
| `Dictionary<TKey, TValue>.ValueCollection.ValueCollection(Dictionary<TKey, TValue>)` | a view of the values; judged with Values | not listed: a constructor of a view is not what code calls |
| `Dictionary<TKey, TValue>.ValueCollection.CopyTo(TValue[], int)` | copies every value into an array (Dictionary.cs 2097-2120) | not listed: copies |
| `Dictionary<TKey, TValue>.ValueCollection.GetEnumerator()` | makes an enumerator that holds the dictionary (Dictionary.cs 2095) | for=TValue |
| `Dictionary<TKey, TValue>.ValueCollection.Count` | the count of the dictionary (Dictionary.cs 2122) | for=TValue |
| `Dictionary<TKey, TValue>.ValueCollection.Enumerator.Dispose()` | does nothing (Dictionary.cs 2220) | for=TValue |
| `Dictionary<TKey, TValue>.ValueCollection.Enumerator.MoveNext()` | copies the value into `_currentValue`, which Current hands out, and reads no key (Dictionary.cs 2222-2242) | for=TValue |
| `Dictionary<TKey, TValue>.ValueCollection.Enumerator.Current` | hands out `_currentValue` (Dictionary.cs 2244) | for=TValue |

### Default Comparers, an Empty Sequence and Immutable Collections

Read at dotnet/runtime `d757c03` (`Lib/System.Collections.Immutable/src/System/Collections/Immutable/`, `Lib/System.Linq/src/System/Linq/`, `src/coreclr/System.Private.CoreLib/src/System/Collections/Generic/`).

| Member | What it does with a value | List entry |
|---|---|---|
| `EqualityComparer<T>.Default`, `Comparer<T>.Default` | the static initializer makes the comparer of the type by reflection (`EqualityComparer.CoreCLR.cs` 13, `Comparer.CoreCLR.cs` 12); no value is read. What a comparer is given is judged where its `Equals`, `GetHashCode` or `Compare` is called | listed |
| `Enumerable.Empty<TResult>()` | `Array.Empty<TResult>()`, an array with no element (`Enumerable.cs` 19-20) | listed |
| `ImmutableArray.Create<T>()` | `ImmutableArray<T>.Empty` (`ImmutableArray.cs` 26-29) | listed |
| `ImmutableArray.Create<T>(T)` | `new[] { item }`: takes the value that it is given into a new array (`ImmutableArray.cs` 37-41), as `List<T>.Add` does | listed |
| `ImmutableArray.CreateBuilder<T>()`, `ImmutableArray.CreateBuilder<T>(int)` | a builder with an empty array (`ImmutableArray.cs` 408-422; `ToBuilder` of an empty array, `ImmutableArray_1.Minimal.cs` 263-269) | listed |
| `ImmutableArray<T>.Length`, `.IsEmpty`, `.IsDefault`, `.IsDefaultOrEmpty` | read the array's length or whether it is there (`ImmutableArray_1.Minimal.cs` 154-198) | listed |
| `ImmutableArray<T>.this[int]` | `return this.array![index]`: hands out a copy of an element, as the indexer of `List<T>` does (`ImmutableArray_1.Minimal.cs` 121-135) | listed |
| `ImmutableArray<T>.ItemRef(int)` | hands out a read-only reference to an element (`ImmutableArray_1.Minimal.cs` 140-148) | listed |
| `ImmutableArray<T>.AsSpan()` | a span over the array (`ImmutableArray_1.cs` 99) | listed |
| `ImmutableArray<T>.GetEnumerator()` | an enumerator that holds the array and an index (`ImmutableArray_1.Minimal.cs` 281-286; the enumerator is a view) | listed |
| `ImmutableArray<T>.Enumerator.MoveNext()`, `.Current` | `++_index < _array.Length`; `Current` hands out a copy of the element (`ImmutableArray_1.Enumerator.cs` 38-65) | listed |
| `ImmutableArray<T>.Builder.Add(T)` | `_elements[_count] = item`, after a growth that moves the elements into a new array that the builder alone holds (`ImmutableArray_1.Builder.cs` 326-332) | listed |
| `ImmutableArray<T>.Builder.Count` | reads the count, or truncates and clears, or grows as `Add` does (`ImmutableArray_1.Builder.cs` 90-130) | listed |
| `ImmutableArray<T>.Builder.MoveToImmutable()` | hands out the builder's array and takes an empty one; throws, keeping the elements, where the capacity is not the count (`ImmutableArray_1.Builder.cs` 203-214) | listed |
| `ImmutableList<T>.Count`, `ImmutableDictionary<TKey, TValue>.Count` | read a field (`ImmutableList_1.cs` 142, `ImmutableDictionary_2.cs` 133-136) | listed |

Not listed, on purpose: `ImmutableArray<T>`'s `Contains`, `IndexOf` and `Remove`, which give elements to `Equals`; `Add`, `Insert`, `SetItem` and the other members that make a new immutable array, which copy every element into it; `ToBuilder` and `ToImmutable` of a builder that holds elements, which copy them; and the other members of the immutable lists and dictionaries, which compare or copy elements.

### `PriorityQueue<TElement, TPriority>`

It is listed with `for=TElement`: the comparer is given priorities only. The heap moves elements within its array, and what is dequeued is taken out.

| Member | What it does with a value | List entry |
|---|---|---|
| `PriorityQueue<TElement, TPriority>.PriorityQueue()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(IComparer<TPriority>?)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(IEnumerable<(TElement Element, TPriority Priority)>)` | copies the source | not listed: copies |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(IEnumerable<(TElement Element, TPriority Priority)>, IComparer<TPriority>?)` | copies the source | not listed: copies |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(int)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.PriorityQueue(int, IComparer<TPriority>?)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Clear()` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Dequeue()` | takes the root out, or hands back the element that it was given (PriorityQueue.cs 244-400) | new;for=TElement |
| `PriorityQueue<TElement, TPriority>.DequeueEnqueue(TElement, TPriority)` | takes the root out, or hands back the element that it was given (PriorityQueue.cs 244-400) | new;for=TElement |
| `PriorityQueue<TElement, TPriority>.Enqueue(TElement, TPriority)` | moves the element into the heap; the comparer is given priorities only (PriorityQueue.cs 198-221, 736-760) | for=TElement |
| `PriorityQueue<TElement, TPriority>.EnqueueDequeue(TElement, TPriority)` | takes the root out, or hands back the element that it was given (PriorityQueue.cs 244-400) | new;for=TElement |
| `PriorityQueue<TElement, TPriority>.EnqueueRange(IEnumerable<(TElement Element, TPriority Priority)>)` | copies every element of a sequence | not listed: copies |
| `PriorityQueue<TElement, TPriority>.EnqueueRange(IEnumerable<TElement>, TPriority)` | copies every element of a sequence | not listed: copies |
| `PriorityQueue<TElement, TPriority>.EnsureCapacity(int)` | no value, or a view of the queue; an enumerator reads by value (PriorityQueue.cs 172-191, 575-625, 944-1064) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Peek()` | reads the root by value (PriorityQueue.cs 229-240, 341-355) | for=TElement |
| `PriorityQueue<TElement, TPriority>.Remove(TElement, out TElement, out TPriority, IEqualityComparer<TElement>?)` | compares elements with an IEqualityComparer<TElement> | not listed: passes a value to a comparer |
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

### `LinkedList<T>` and `LinkedListNode<T>`

A node holds its value until it is assigned again, also after it is removed from the list. `Find`, `FindLast`, `Contains` and `Remove(T)` compare values and are not listed.

| Member | What it does with a value | List entry |
|---|---|---|
| `LinkedList<T>.LinkedList()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.LinkedList(IEnumerable<T>)` | copies the source | not listed: copies |
| `LinkedList<T>.AddAfter(LinkedListNode<T>, LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddAfter(LinkedListNode<T>, T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddBefore(LinkedListNode<T>, LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddBefore(LinkedListNode<T>, T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddFirst(LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddFirst(T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddLast(LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.AddLast(T)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Clear()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Contains(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | not listed: passes a value to Equals |
| `LinkedList<T>.CopyTo(T[], int)` | copies every value | not listed: copies |
| `LinkedList<T>.Find(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | not listed: passes a value to Equals |
| `LinkedList<T>.FindLast(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | not listed: passes a value to Equals |
| `LinkedList<T>.GetEnumerator()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.GetObjectData(SerializationInfo, StreamingContext)` | serializes or restores every value | not listed: copies |
| `LinkedList<T>.OnDeserialization(object?)` | serializes or restores every value | not listed: copies |
| `LinkedList<T>.Remove(LinkedListNode<T>)` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Remove(T)` | compares values with EqualityComparer<T>.Default (LinkedList.cs 221-300) | not listed: passes a value to Equals |
| `LinkedList<T>.RemoveFirst()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.RemoveLast()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Count` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.First` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Last` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Enumerator.Dispose()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Enumerator.MoveNext()` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |
| `LinkedList<T>.Enumerator.Current` | adds, removes or walks nodes; a value goes into a new node once (LinkedList.cs 28-320) | listed |

| Member | What it does with a value | List entry |
|---|---|---|
| `LinkedListNode<T>.LinkedListNode(T)` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.List` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.Next` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.Previous` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |
| `LinkedListNode<T>.Value` | reads or assigns the value of the node (LinkedList.cs 620-626) | listed |
| `LinkedListNode<T>.ValueRef` | a node and its links; ValueRef is a reference to the value (LinkedList.cs 594-627) | listed |

### `StrongBox<T>`

The constructors are listed. Its `Value` is a public field, which a list cannot name; it is reached in place, like a field of any object.

| Member | What it does with a value | List entry |
|---|---|---|
| `StrongBox<T>.Value` | a public field | not listed: a field cannot be an entry; it is reached in place like a field of any object |
| `StrongBox<T>.StrongBox()` | stores the value once (StrongBox.cs) | listed |
| `StrongBox<T>.StrongBox(T)` | stores the value once (StrongBox.cs) | listed |

### `SortedDictionary<TKey, TValue>`

It is not listed: it compares what it holds. Every lookup gives each pair that it passes, value included, to its comparer by value.

| Member | What it does with a value | List entry |
|---|---|---|
| `T:System.Collections.Generic.SortedDictionary`2` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.SortedDictionary()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.SortedDictionary(IComparer<TKey>?)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.SortedDictionary(IDictionary<TKey, TValue>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `SortedDictionary<TKey, TValue>.SortedDictionary(IDictionary<TKey, TValue>, IComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `SortedDictionary<TKey, TValue>.Add(TKey, TValue)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Clear()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ContainsKey(TKey)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ContainsValue(TValue)` | EqualityComparer<TValue>.Default.Equals on every value (Dictionary.cs 300-368) | not listed: passes a value to Equals |
| `SortedDictionary<TKey, TValue>.CopyTo(KeyValuePair<TKey, TValue>[], int)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.GetEnumerator()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Remove(TKey)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Comparer` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Count` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.this[TKey]` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Keys` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Values` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.Enumerator` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Enumerator.Dispose()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Enumerator.MoveNext()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.Enumerator.Current` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.KeyCollection` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.KeyCollection(SortedDictionary<TKey, TValue>)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.CopyTo(TKey[], int)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.GetEnumerator()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Contains(TKey)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Count` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.KeyCollection.Enumerator` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Enumerator.Dispose()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Enumerator.MoveNext()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.KeyCollection.Enumerator.Current` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.ValueCollection` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.ValueCollection(SortedDictionary<TKey, TValue>)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.CopyTo(TValue[], int)` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.GetEnumerator()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Count` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `T:System.Collections.Generic.SortedDictionary`2.ValueCollection.Enumerator` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Enumerator.Dispose()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Enumerator.MoveNext()` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |
| `SortedDictionary<TKey, TValue>.ValueCollection.Enumerator.Current` | every lookup gives each pair that it passes, value included, to KeyValuePairComparer.Compare by value (SortedDictionary.cs 106-269, 836-848) | not listed: passes a value to a comparer |

### `SortedList<TKey, TValue>`

It is listed with `for=TValue`: the keys and the values are two arrays, and only the keys are searched. `ContainsValue`, `IndexOfValue` and `Values` compare or copy values and are not listed.

| Member | What it does with a value | List entry |
|---|---|---|
| `SortedList<TKey, TValue>.SortedList()` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.SortedList(IComparer<TKey>?)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.SortedList(IDictionary<TKey, TValue>)` | copies the source | not listed: copies |
| `SortedList<TKey, TValue>.SortedList(IDictionary<TKey, TValue>, IComparer<TKey>?)` | copies the source | not listed: copies |
| `SortedList<TKey, TValue>.SortedList(int)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.SortedList(int, IComparer<TKey>?)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Add(TKey, TValue)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.Clear()` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.ContainsKey(TKey)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.ContainsValue(TValue)` | compares values (Array.IndexOf, SortedList.cs 425, 651) | not listed: passes a value to Equals |
| `SortedList<TKey, TValue>.GetEnumerator()` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.GetKeyAtIndex(int)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.GetValueAtIndex(int)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.IndexOfKey(TKey)` | keys and values are two arrays; only the keys are searched, and inserting or removing shifts both (SortedList.cs 179-740) | for=TValue |
| `SortedList<TKey, TValue>.IndexOfValue(TValue)` | compares values (Array.IndexOf, SortedList.cs 425, 651) | not listed: passes a value to Equals |
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
| `SortedList<TKey, TValue>.Values` | a list that hands out every value by value, CopyTo included | not listed: copies |

### `FrozenDictionary<TKey, TValue>`

It is listed with `for=TValue`: keys and values are two arrays, and only the keys are compared. Making one copies every value of its source, so none of the ways to make one is listed, and neither are `Values` and `CopyTo`.

| Member | What it does with a value | List entry |
|---|---|---|
| `FrozenDictionary<TKey, TValue>.ContainsKey(TKey)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.CopyTo(KeyValuePair<TKey, TValue>[], int)` | copies every value | not listed: copies |
| `FrozenDictionary<TKey, TValue>.CopyTo(Span<KeyValuePair<TKey, TValue>>)` | copies every value | not listed: copies |
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
| `FrozenDictionary<TKey, TValue>.Values` | copies every value | not listed: copies |
| `FrozenDictionary<TKey, TValue>.Enumerator.MoveNext()` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.Enumerator.Current` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `T:System.Collections.Frozen.FrozenDictionary`2.AlternateLookup`1` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | view |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.ContainsKey(TAlternateKey)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Dictionary` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |
| `FrozenDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.this[TAlternateKey]` | looks up by key in an array of keys and hands out the value or a reference to it (DefaultFrozenDictionary.cs 21-34 and the others) | for=TValue |

| Member | What it does with a value | List entry |
|---|---|---|
| `FrozenDictionary.Create<TKey, TValue>(params ReadOnlySpan<KeyValuePair<TKey, TValue>>)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | not listed: copies |
| `FrozenDictionary.Create<TKey, TValue>(IEqualityComparer<TKey>?, params ReadOnlySpan<KeyValuePair<TKey, TValue>>)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | not listed: copies |
| `FrozenDictionary.ToFrozenDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | not listed: copies |
| `FrozenDictionary.ToFrozenDictionary<TSource, TKey>(IEnumerable<TSource>, Func<TSource, TKey>, IEqualityComparer<TKey>?)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | not listed: copies |
| `FrozenDictionary.ToFrozenDictionary<TSource, TKey, TElement>(IEnumerable<TSource>, Func<TSource, TKey>, Func<TSource, TElement>, IEqualityComparer<TKey>?)` | makes a frozen dictionary by copying every value of the source into a dictionary and then into arrays (FrozenDictionary.cs 42-160) | not listed: copies |

### Key and Value Collections That Are Erasable for the Other Type

An `erasable` line with `for=` says that no base type and no interface of the type hands out a value of the type parameters that it
names. For the claim to hold, the code behind those interfaces must also make no copy of such a value: a conversion to the interface
hands the object to code that the analyzer does not see. So each line below rests on two readings: the declaration of the type, and
every member that implements one of its interfaces. `ErasableLines_NameTypesWhoseBaseTypesAndInterfacesMentionNoTypeParameterThatTheyAreFor`
in `SafeListTests` checks the first against the reference packs of .NET 10 and .NET 11.

**What the interfaces hand out, exactly.** The generic interfaces of the key collection hand out keys, and those of the value
collection values. The non-generic `ICollection` hands out elements through `CopyTo(Array, int)`, which copies keys or values only, and
one more object: `ICollection.SyncRoot`. Both collections return `((ICollection)_dictionary).SyncRoot`, and the dictionary returns
`this` (Dictionary.cs 1634, 2009, 2203 / 1742, 2117, 2311). So on .NET the lock object of a key or a value collection is the dictionary
itself, and code that casts it back to `Dictionary<TKey, TValue>` reaches the other half of every entry. The .NET Framework makes the
lock object of a dictionary a `new Object()` (netfx dictionary.cs 591-597, 917-918, 1103-1104), which reaches nothing. The route takes
a cast to a legacy interface, and the use of a property that `ICollection` documents as an object to synchronize access with, as the
dictionary itself: it is an API used against its documentation, which the analyzer does not follow (see What Is Not Covered), and the
lines stay.

**How the candidates were found.** Every public generic class and interface of the reference packs of .NET 10 (10.0.14) and .NET 11
(11.0.0) was listed with the type parameters, its own or those of a type that contains it, that no base type and no interface of it
mentions. That gave the types below, and the options types of `Microsoft.Extensions.Options` in the pack of .NET 11.

**Read at the release tags `v10.0.0` and `v11.0.0`,** where the line numbers differ by `v11.0.0` second, and in the .NET Framework
reference source (microsoft/referencesource, branch `main`, `mscorlib/system/collections/generic/dictionary.cs`).

| Type | Interfaces | What the code behind them reads | Source | List entry |
|---|---|---|---|---|
| `Dictionary<TKey, TValue>.KeyCollection` | `ICollection<TKey>`, `IReadOnlyCollection<TKey>`, `ICollection` | `CopyTo` and `ICollection.CopyTo` read `entries[i].key`, a field of the array element; `Contains` is `ContainsKey`, which compares keys through `ref Entry`; the enumerator takes `ref Entry entry` and sets `_currentKey = entry.key`; `Add`, `Remove` and `Clear` throw. No value is read, and no whole entry is copied. `ICollection.SyncRoot` is the dictionary's, the dictionary itself on .NET (see above) | Dictionary.cs 1886, 1923, 1937-1938, 1997, 2037-2041 / 1994, 2031, 2045-2046, 2105, 2145-2149; `FindValue` 297-298; netfx dictionary.cs 801, 832, 853, 904, 946 | `erasable;for=TValue` |
| `Dictionary<TKey, TValue>.ValueCollection` | `ICollection<TValue>`, `IReadOnlyCollection<TValue>`, `ICollection` | `CopyTo` and `ICollection.CopyTo` read `entries[i].value`; `Contains` is `ContainsValue`, which compares values only; the enumerator takes `ref Entry entry` and sets `_currentValue = entry.value`; `Add`, `Remove` and `Clear` throw. No key is read. `ICollection.SyncRoot` is the dictionary's, the dictionary itself on .NET (see above) | Dictionary.cs 2081, 2118, 2138, 2191, 2231-2235, `ContainsValue` 300-339 / 2189, 2226, 2246, 2299, 2339-2343; netfx dictionary.cs 988, 1019, 1045, 1090, 1132 | `erasable;for=TKey` |
| `OrderedDictionary<TKey, TValue>.KeyCollection`, `.ValueCollection` | `IList<T>`, `IReadOnlyList<T>`, `IList` | the enumerators wrap the dictionary's, whose `MoveNext` makes `new KeyValuePair<TKey, TValue>(entry.Key, entry.Value)`, and the indexers call `GetAt`, which makes `new(e.Key, e.Value)`: both copy the other half | OrderedDictionary.cs 599, 1439, 1467, 1643, 1662, 1887 / 599, 1440, 1468, 1644, 1663, 1888 | not listed |
| `SortedDictionary<TKey, TValue>.KeyCollection`, `.ValueCollection` | `ICollection<T>`, `IReadOnlyCollection<T>`, `ICollection` | `Current` is `_dictEnum.Current.Key` or `.Value`, and the dictionary's `Current` hands out the whole `KeyValuePair<TKey, TValue>` by value: the other half is copied | SortedDictionary.cs 432, 515, 650, 676, 811 (same at both tags) | not listed |
| `ReadOnlyDictionary<TKey, TValue>.KeyCollection`, `.ValueCollection` | `ICollection<T>`, `IReadOnlyCollection<T>`, `ICollection` | wrap the `Keys` or `Values` of whatever `IDictionary<TKey, TValue>` the dictionary was made with, whose code is not known | ReadOnlyDictionary.cs 38-40, 247-255, 287, 303-311, 339 (same at both tags) | not listed |
| `Lazy<T, TMetadata>` | none; its base `Lazy<T>` has none either | `Lazy<T>` has no field of the metadata and never sees it, and `Lazy<T, TMetadata>` overrides nothing: only `Metadata` hands it out. The class of the .NET Framework, in System.ComponentModel.Composition, has the same shape in its metadata: one field, the constructors and `get_Metadata`, no interface and no override | LazyOfTTMetadata.cs (the same file at both tags), Lazy.cs 186-505; netfx mscorlib/system/Lazy.cs 59 | `erasable;for=TMetadata` |
| `IGrouping<TKey, TElement>`, `ISignatureTypeProvider<TType, TGenericContext>` | interfaces | the code behind an interface is whatever implements it | | not listed |
| `KeyedCollection<TKey, TItem>` | `Collection<TItem>` and its interfaces | adding an item through `IList<TItem>` runs `InsertItem`, which puts the key that `GetKeyForItem` gives into its dictionary: a copy of the key | | not listed |
| `ConfigureNamedOptions<TOptions, TDep...>`, `PostConfigureOptions<...>`, `ValidateOptions<...>` and `AsyncValidateOptions<...>` (.NET 11) | `IConfigureNamedOptions<TOptions>` and the like | pass each dependency by value to the delegate that they were made with | ConfigureNamedOptions.cs 63, 107; PostConfigureOptions.cs 101; ValidateOptions.cs 122 (v11.0.0) | not listed |

The other dictionaries need no line: `ConcurrentDictionary<,>.Keys` and `.Values` are typed `ICollection<T>`, `SortedList<,>.Keys` and
`.Values` `IList<T>`, the `Keys` and `Values` of `ImmutableDictionary<,>` and `ImmutableSortedDictionary<,>` `IEnumerable<T>`, and
those of `FrozenDictionary<,>` `ImmutableArray<T>`. None of these types names the other type parameter, so a conversion of it hides
nothing of it.

## Channels and Concurrent Collections

These types are on the list because low-level libraries use them often: a trial in which five libraries were written in the ordinary way found that about a third of what the analyzer met was a member of these types that was not on the list. They are `System.Threading.Channels`, `System.Collections.Concurrent` and the keys of `Dictionary<TKey, TValue>` (see [`Dictionary<TKey, TValue>`](#dictionarytkey-tvalue)).

Every public member of each type is here: the tables list 91 members, apart from the three of `ConcurrentDictionary<,>` that the full table below shows to be listed, and the rest are not listed, with the reason.

Markers: `new` = what the member hands out is a new value whatever it was called on; `borrows` = the member keeps nothing of a container that it is given; `borrowswhen` = the member keeps nothing of a value that it is given in the outcomes that it lists (see [Members That Keep Nothing of a Value in Some Outcomes](#members-that-keep-nothing-of-a-value-in-some-outcomes)); none = what it hands out is as new as what it was called on, so on a stored collection it is a copy and gets a warning (`SCA1030`). `for=TValue` is on every entry of the two dictionaries, as for `Dictionary<,>`.

### `System.Threading.Channels`

| Member | What it does with a value | Marker |
|---|---|---|
| `Channel.CreateUnbounded<T>()`, `(UnboundedChannelOptions)` | makes a channel; no value | none |
| `Channel.CreateBounded<T>(int)`, `(BoundedChannelOptions)` | makes a channel; no value | none |
| `Channel.CreateBounded<T>(BoundedChannelOptions, Action<T>)` | keeps the callback; a bounded channel in a dropping mode hands each item that it drops to it once (`BoundedChannel.cs` 424, 440) | none |
| `Channel.CreateUnboundedPrioritized<T>()`, `(options)` | orders items with an `IComparer<T>`, which is given two items by value for each comparison | **not listed**: a copy of each item goes to the comparer |
| `Channel<TWrite,TRead>.Reader`, `.Writer` | hands out the reader or the writer object | none |
| `Channel<,>` → `ChannelReader<>`, → `ChannelWriter<>` (implicit) | `channel.Reader`, `channel.Writer` | none |
| `ChannelReader<T>.TryRead(out T)` | dequeues: `_items.TryDequeue(out item)` (unbounded), `DequeueItemAndPostProcess()` (bounded). Nothing that another read could hand out is left | `new` |
| `ChannelReader<T>.ReadAsync(ct)` | dequeues, or parks a reader that one writer completes with its item (`TryWrite` → `blockedReader.DangerousSetResult(item)`); a reader that was cancelled is skipped and the item goes to the next or to the buffer | `new` |
| `ChannelReader<T>.TryPeek(out T)` | `_items.TryPeek`, `PeekHead()`: a copy of what the channel still holds | none |
| `ChannelReader<T>.WaitToReadAsync(ct)` | no value | none |
| `ChannelReader<T>.Completion`, `.Count`, `.CanCount`, `.CanPeek` | no value | none |
| `ChannelReader<T>.ReadAllAsync(ct)` | an async iterator: `while (await WaitToReadAsync) while (TryRead(out item)) yield return item;`. Each element is taken out of the channel | `drains`: the sequence is a container, and `new` says nothing of a container (see [Sequences That Drain](#sequences-that-drain)) |
| `ChannelWriter<T>.TryWrite(T)` | moves the item into one place: the buffer, a parked reader, or the drop callback. On `false` (closed, or full in wait mode) it has not taken it (see [Members That Keep Nothing of a Value in Some Outcomes](#members-that-keep-nothing-of-a-value-in-some-outcomes)) | `borrowswhen=false` |
| `ChannelWriter<T>.WriteAsync(T, ct)` | `TryWrite`, or in a full bounded channel parks the item in one waiting writer, from which a following read moves it into the buffer (`BoundedChannel.cs` 268-273). Cancelled or closed: the item is dropped with the exception | none |
| `ChannelWriter<T>.WaitToWriteAsync(ct)`, `.TryComplete(Exception)`, `.Complete(Exception)` | no value | none |

### `ConcurrentQueue<T>`, `ConcurrentStack<T>` and `ConcurrentBag<T>`

`ConcurrentQueue<T>`:

| Member | What it does with a value | Marker |
|---|---|---|
| `ConcurrentQueue<T>()` | empty queue | none |
| `ConcurrentQueue<T>.Enqueue(T)` | stores the item in a segment slot | none |
| `ConcurrentQueue<T>.TryDequeue(out T)` | takes the item out of its slot (`ConcurrentQueueSegment.cs` 139) | `new` |
| `ConcurrentQueue<T>.TryPeek(out T)` | a copy of the head | none |
| `ConcurrentQueue<T>.Count`, `.IsEmpty`, `.Clear()` | no value handed out; `Clear` drops what is held | none |
| `ConcurrentQueue<T>.GetEnumerator()` | enumerates a snapshot: each `Current` is a copy of an element that the queue still holds | none |
| `ConcurrentQueue<T>(IEnumerable<T>)`, `.ToArray()`, `.CopyTo(T[], int)` | copy every element | **not listed**: copies |

`ConcurrentStack<T>`:

| Member | What it does with a value | Marker |
|---|---|---|
| `ConcurrentStack<T>()` | empty stack | none |
| `ConcurrentStack<T>.Push(T)` | stores the item in a node | none |
| `ConcurrentStack<T>.TryPop(out T)` | unlinks the head node and hands out its value | `new` |
| `ConcurrentStack<T>.TryPopRange(T[])`, `(T[], int, int)` | unlinks nodes and writes their values into the array (`CopyRemovedItems`); keeps nothing of the array | `borrows` |
| `ConcurrentStack<T>.TryPeek(out T)` | a copy of the head | none |
| `ConcurrentStack<T>.Count`, `.IsEmpty`, `.Clear()`, `.GetEnumerator()` | as for the queue | none |
| `ConcurrentStack<T>(IEnumerable<T>)`, `.PushRange(T[])`, `(T[], int, int)`, `.ToArray()`, `.CopyTo` | copy every element; after `PushRange` the array still holds what the stack now holds too | **not listed**: copies |

`ConcurrentBag<T>`:

| Member | What it does with a value | Marker |
|---|---|---|
| `ConcurrentBag<T>()` | empty bag | none |
| `ConcurrentBag<T>.Add(T)` | stores the item in the thread's queue | none |
| `ConcurrentBag<T>.TryTake(out T)` | `TryLocalPop` or `TrySteal(take: true)`: removes | `new` |
| `ConcurrentBag<T>.TryPeek(out T)` | `TryLocalPeek` or `TrySteal(take: false)`: a copy | none |
| `ConcurrentBag<T>.Count`, `.IsEmpty`, `.Clear()` | no value handed out | none |
| `ConcurrentBag<T>.GetEnumerator()` | `ToArray()` and enumerates that: copies of what the bag still holds | none |
| `ConcurrentBag<T>(IEnumerable<T>)`, `.ToArray()`, `.CopyTo` | copy every element | **not listed**: copies |

### `ConcurrentDictionary<TKey, TValue>`

| Member | What it does with a value | Marker |
|---|---|---|
| `ConcurrentDictionary<,>()`, `(int, int)`, `(IEqualityComparer<TKey>)`, `(int, int, IEqualityComparer<TKey>)` | empty dictionary | none |
| `ConcurrentDictionary<,>.TryAdd(TKey, TValue)` | `TryAddInternal(..., updateIfExists: false)`: stores the value, or returns false and has not taken it | `borrowswhen=false` |
| `ConcurrentDictionary<,>.TryRemove(TKey, out TValue)` | removes the node and hands out its value | `new` |
| `ConcurrentDictionary<,>.TryGetValue(TKey, out TValue)` | a copy of the stored value | none |
| `ConcurrentDictionary<,>.GetOrAdd(TKey, TValue)` | returns the stored value, or stores the argument and returns it: either way a copy of what the dictionary holds afterwards | none |
| `ConcurrentDictionary<,>.this[TKey]` | get: a copy of the stored value. set: stores the value in place of the old one | none |
| `ConcurrentDictionary<,>.ContainsKey(TKey)`, `.Count`, `.IsEmpty`, `.Clear()` | no value handed out | none |
| `ConcurrentDictionary<,>.Keys` | a snapshot of the keys as an `ICollection<TKey>` (`GetKeys`, line 2129): no value is in it | none |
| `ConcurrentDictionary<,>.GetEnumerator()` | each `Current` is a pair with a copy of the stored value | none |
| `ConcurrentDictionary<,>.TryRemove(KeyValuePair<,>)`, `.TryUpdate(TKey, TValue, TValue)` | compare values with `EqualityComparer<TValue>.Default`, which is given a copy of each by value and boxes it for a struct with no `IEquatable<T>` | **not listed**: copies |
| `ConcurrentDictionary<,>.AddOrUpdate` (3 overloads) | the update factory is given the stored value by value, and the result is compared as in `TryUpdate` | **not listed**: copies |
| `ConcurrentDictionary<,>.GetOrAdd(TKey, Func<TKey,TValue>)`, `GetOrAdd<TArg>(...)` | nothing copies, but what the factory made is dropped when another thread added first, and the result is a second instance as for `GetOrAdd(TKey, TValue)` | **not listed**: no use of it with a guard is clean |
| `ConcurrentDictionary<,>.Values`, `.ToArray()`, the 3 constructors from a sequence | copy every value | **not listed**: copies |
| `ConcurrentDictionary<,>.GetAlternateLookup`, `.TryGetAlternateLookup` (.NET 9) | hand out a lookup struct with members of its own | listed, with `for=TValue`: the lookup is a `view`, and its members are in the full table below |
| `ConcurrentDictionary<,>.Comparer` | no value | listed, with `for=TValue`, as for `Dictionary<,>` |

Every public member:

| Member | What it does with a value | List entry |
|---|---|---|
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary()` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>>)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | copies every value of its source (Dictionary.cs 85-100, AddRange) | not listed: copies |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(IEqualityComparer<TKey>?)` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(int, IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)` | copies the source | not listed: copies |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(int, int)` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ConcurrentDictionary(int, int, IEqualityComparer<TKey>?)` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AddOrUpdate(TKey, Func<TKey, TValue>, Func<TKey, TValue, TValue>)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | not listed: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.AddOrUpdate(TKey, TValue, Func<TKey, TValue, TValue>)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | not listed: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.AddOrUpdate<TArg>(TKey, Func<TKey, TArg, TValue>, Func<TKey, TValue, TArg, TValue>, TArg)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | not listed: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.Clear()` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.ContainsKey(TKey)` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetAlternateLookup<TAlternateKey>()` | hands out a lookup that holds the dictionary | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetEnumerator()` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetOrAdd(TKey, Func<TKey, TValue>)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | not listed: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.GetOrAdd(TKey, TValue)` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.GetOrAdd<TArg>(TKey, Func<TKey, TArg, TValue>, TArg)` | passes the stored value or a value made by a delegate to a delegate, and a racing call may run it and drop the result (ConcurrentDictionary.cs 1180-1420) | not listed: passes a value to a delegate |
| `ConcurrentDictionary<TKey, TValue>.ToArray()` | copies every value | not listed: copies |
| `ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue)` | judged with the concurrent collections | borrowswhen=false;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryGetAlternateLookup<TAlternateKey>(out ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>)` | hands out a lookup that holds the dictionary | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryRemove(KeyValuePair<TKey, TValue>)` | compares the stored value with EqualityComparer<TValue>.Default (ConcurrentDictionary.cs 413-470, 620-700) | not listed: passes a value to Equals |
| `ConcurrentDictionary<TKey, TValue>.TryRemove(TKey, out TValue)` | judged with the concurrent collections | new;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.TryUpdate(TKey, TValue, TValue)` | compares the stored value with EqualityComparer<TValue>.Default (ConcurrentDictionary.cs 413-470, 620-700) | not listed: passes a value to Equals |
| `ConcurrentDictionary<TKey, TValue>.Comparer` | the comparer of keys | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.Count` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.IsEmpty` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.this[TKey]` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.Keys` | judged with the concurrent collections | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.Values` | copies every value | not listed: copies |
| `T:System.Collections.Concurrent.ConcurrentDictionary`2.AlternateLookup`1` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | view |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.ContainsKey(TAlternateKey)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryAdd(TAlternateKey, TValue)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | borrowswhen=false;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TKey, out TValue)` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryRemove(TAlternateKey, out TValue)` | unlinks the node and hands out its value (ConcurrentDictionary.cs 2585-2650) | new;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryRemove(TAlternateKey, out TKey, out TValue)` | unlinks the node and hands out its value (ConcurrentDictionary.cs 2585-2650) | new;for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.Dictionary` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |
| `ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.this[TAlternateKey]` | what the dictionary's own members do, by the alternate key (ConcurrentDictionary.cs 2337-2600) | for=TValue |

### `BlockingCollection<T>`

| Member | What it does with a value | Marker |
|---|---|---|
| `BlockingCollection<T>()`, `(int)` | over a `ConcurrentQueue<T>` of its own | none |
| `BlockingCollection<T>.Add(T)`, `(T, ct)`, `.TryAdd(T)`, `(T, TimeSpan)`, `(T, int)`, `(T, int, ct)` | `_collection.TryAdd(item)` (line 475): moves the item in; a `TryAdd` that returns false has not taken it (see [Members That Keep Nothing of a Value in Some Outcomes](#members-that-keep-nothing-of-a-value-in-some-outcomes)) | none on the two `Add`; `borrowswhen=false` on the four `TryAdd` |
| `BlockingCollection<T>.Take()`, `(ct)`, `.TryTake(out T)` and its 3 overloads | `_collection.TryTake(out item)` (line 696): removes | `new` |
| `BlockingCollection<T>.CompleteAdding()`, `.IsAddingCompleted`, `.IsCompleted`, `.Count`, `.BoundedCapacity`, `.Dispose()` | no value; `Dispose` releases the semaphores and leaves what is held | none |
| `BlockingCollection<T>(IProducerConsumerCollection<T>)`, `(..., int)` | wraps another collection | **not listed**: what `Add` and `Take` then do is what that collection does |
| `BlockingCollection<T>.AddToAny`, `.TryAddToAny` (6), `.TakeFromAny`, `.TryTakeFromAny` (6) | add to or take from one of several collections | **not listed**: the array of collections is a parameter whose type mentions `T`, which needs `borrows`, and the taking ones need `new` as well; one entry carries one marker |
| `BlockingCollection<T>.GetConsumingEnumerable()`, `(ct)` | a sequence that takes each element out as it is enumerated | `drains`: as `ReadAllAsync` (see [Sequences That Drain](#sequences-that-drain)) |
| `BlockingCollection<T>.ToArray()`, `.CopyTo` | copy every element | **not listed**: copies |

### Sequences That Drain

Read at the release tags `v10.0.0` and `v11.0.0`, the frameworks that the analyzer is tested with. Line numbers are the same at both tags unless a second number is given. In `TaskAsyncEnumerableExtensions.cs` and `ConfiguredCancelableAsyncEnumerable.cs` the two tags differ only in the `#if` around `allows ref struct` (`NET10_0_OR_GREATER` and `NET`).

A method is marked `drains` when the sequence that it returns takes each element out of its source as it hands it out, so that no element is handed out twice by anything (see [Symbol List Format](../../Docs/articles/rules/symbol-list-format.md)).

| Member | What it does with a value | Source | List entry |
|---|---|---|---|
| `ChannelReader<T>.ReadAllAsync(CancellationToken)` | an async iterator: `while (await WaitToReadAsync(...)) while (TryRead(out T? item)) yield return item;`. Each element that it yields is one that `TryRead` has just taken out of the channel, and `TryRead` of every channel of the framework dequeues (see the tables above). It reads nothing else. No reader of the framework overrides it: `BoundedChannel.cs`, `UnboundedChannel.cs`, `SingleConsumerUnboundedChannel.cs`, `UnboundedPriorityChannel.cs` and `RendezvousChannel.cs` do not mention it | ChannelReader.cs:103-112; `TryRead` is abstract at :36 | `drains` |
| `BlockingCollection<T>.GetConsumingEnumerable()` | `return GetConsumingEnumerable(CancellationToken.None);` | BlockingCollection.cs:1598-1601 (v11.0.0: 1599-1602) | `drains` |
| `BlockingCollection<T>.GetConsumingEnumerable(CancellationToken)` | an iterator: `while (TryTakeWithNoTimeValidation(out T? item, Timeout.Infinite, ...)) yield return item;`. `TryTakeWithNoTimeValidation` waits for the semaphore of occupied nodes and then calls `_collection.TryTake(out item)`, which removes. The two listed constructors make that collection a `ConcurrentQueue<T>`, whose `TryTake` is `TryDequeue` (see the tables above) | BlockingCollection.cs:1612-1624 (v11.0.0: 1613-1625), :645-696, constructors at :146-147 and :161-162 | `drains` |

What the iterators keep: the compiler keeps the yielded element in the state machine until the next `MoveNext`, so an enumerator that is driven by hand and read twice at one element hands out a copy, as [Built-in Framework Symbol List](../../Docs/articles/rules/framework-symbol-list.md#sequences-that-drain) says. The marker describes the sequence as the call returns it.

Two points that the marker rests on, as any entry for a virtual or an interface member does:

- `ReadAllAsync` is virtual and calls the abstract `TryRead`, so for a reader of another library it holds as far as that reader's `TryRead`, and any override of `ReadAllAsync`, do what the documentation of `ReadAllAsync` says: each `MoveNextAsync` that returns `true` "will read the next item out of the channel" (ChannelReader.cs:99).
- `GetConsumingEnumerable` takes from the collection that the `BlockingCollection<T>` wraps. With a collection given to the constructor that takes an `IProducerConsumerCollection<T>`, which is not on the list, it holds as far as that collection's `TryTake` removes, as `IProducerConsumerCollection<T>.TryTake` is documented to do.

## Interfaces

The members are judged by their specification, at a basic level.

| Member | Listed | Reason |
|---|---|---|
| `IEnumerable<T>.GetEnumerator`, `IEnumerator<T>.Current`, `IEnumerator.MoveNext`, `IDisposable.Dispose` | listed | `foreach` over a typed sequence; `MoveNext` and `Dispose` hand out nothing |
| `ICollection<T>.Count`, `IsReadOnly`, `Add`, `Clear` | listed | Store or count |
| `ICollection<T>.Contains`, `Remove`, `CopyTo` | not listed | Specified to compare or copy |
| `IList<T>.Item`, `Insert`, `RemoveAt` | listed | Hand out or store |
| `IList<T>.IndexOf` | not listed | Compares |
| `IReadOnlyCollection<T>.Count`, `IReadOnlyList<T>.Item` | listed | |
| `IDictionary<TKey, TValue>.Item`, `Add`, `Remove(TKey)`, `TryGetValue`, `ContainsKey`; `IReadOnlyDictionary<TKey, TValue>.Item`, `TryGetValue`, `ContainsKey` | listed | Judged for `TValue`, with `for=TValue` |
| `IDictionary.Keys`/`Values` | not listed | Not read |
| `IAsyncEnumerable<T>.GetAsyncEnumerator`, `IAsyncEnumerator<T>.Current`, `MoveNextAsync`, `IAsyncDisposable.DisposeAsync` | listed | `await foreach` |

### Async Sequences Behind `ConfigureAwait` and `WithCancellation`

`await foreach` over `sequence.ConfigureAwait(false)` or `sequence.WithCancellation(ct)` of an `IAsyncEnumerable<T>` binds these members. Each holds or passes on a reference to the sequence or to its enumerator and a flag or a token; none touches a value of `T`, except `Current`, which hands out what the wrapped enumerator hands out. So no entry has a marker: what comes out is as new as what went in.

| Member | What it does with a value | Source | List entry |
|---|---|---|---|
| `TaskAsyncEnumerableExtensions.ConfigureAwait<T>(IAsyncEnumerable<T>, bool)` | `new ConfiguredCancelableAsyncEnumerable<T>(source, continueOnCapturedContext, cancellationToken: default)`: wraps the reference to the sequence | TaskAsyncEnumerableExtensions.cs:24-29 | listed |
| `TaskAsyncEnumerableExtensions.WithCancellation<T>(IAsyncEnumerable<T>, CancellationToken)` | `new ConfiguredCancelableAsyncEnumerable<T>(source, continueOnCapturedContext: true, cancellationToken)`: the same | TaskAsyncEnumerableExtensions.cs:36-41 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.ConfigureAwait(bool)` | a new struct over the same `_enumerable` and `_cancellationToken`, with the new flag. The struct has three fields: the sequence, the token and the flag (:17-19) | ConfiguredCancelableAsyncEnumerable.cs:32-33 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.WithCancellation(CancellationToken)` | a new struct over the same `_enumerable` and flag, with the new token | ConfiguredCancelableAsyncEnumerable.cs:39-40 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.GetAsyncEnumerator()` | `new Enumerator(_enumerable.GetAsyncEnumerator(_cancellationToken), _continueOnCapturedContext)`: calls the sequence's own `GetAsyncEnumerator`, which an `await foreach` over the sequence calls as well, and wraps the enumerator | ConfiguredCancelableAsyncEnumerable.cs:44-47 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.Enumerator.MoveNextAsync()` | `_enumerator.MoveNextAsync().ConfigureAwait(_continueOnCapturedContext)`: a `ValueTask<bool>`; no value of `T`. The enumerator struct has two fields, the enumerator and the flag (:53-54) | ConfiguredCancelableAsyncEnumerable.cs:68-69 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.Enumerator.Current` | `_enumerator.Current`: returns what the wrapped enumerator's `Current` returns, which is the hand-out; it keeps nothing of it | ConfiguredCancelableAsyncEnumerable.cs:72 | listed |
| `ConfiguredCancelableAsyncEnumerable<T>.Enumerator.DisposeAsync()` | `_enumerator.DisposeAsync().ConfigureAwait(_continueOnCapturedContext)`: no value of `T` | ConfiguredCancelableAsyncEnumerable.cs:78-79 | listed |

Neither `ConfiguredCancelableAsyncEnumerable<T>` nor its `Enumerator` holds a value of `T` inline, but neither is on the list as a `view`, so over a non-copyable `T` both count as non-copyable structs. That can only add warnings, where one of the two structs is copied.

## Generic Structs

None of these is a view.

| Member | Listed | Reason | Source |
|---|---|---|---|
| `Nullable<T>`: `#ctor(T)`, `HasValue`, `Value`, `GetValueOrDefault()`, `GetValueOrDefault(T)`, implicit conversion from `T`, explicit conversion to `T` | listed | Store or hand out | CL/Nullable.cs:27-74 |
| `Nullable<T>`: `Equals`, `GetHashCode`, `ToString` | not listed | Call user code on the value (in place) | :58-67 |
| `Nullable.Compare<T>`, `Nullable.Equals<T>` | not listed | Comparer by value | :79-94 |
| `KeyValuePair<TKey, TValue>`: `#ctor`, `Key`, `Value`, `Deconstruct`; `KeyValuePair.Create` | listed | Store or hand out | CL/Collections/Generic/KeyValuePair.cs:15-51 |
| `KeyValuePair<TKey, TValue>.ToString` | not listed | Passes both by value | :45 |
| `ValueTuple<…>` constructors (arities 1 to 8), `ValueTuple.Create` (arities 1 to 8) | listed | `Item1 = item1; …` | CL/ValueTuple.cs:445 and the others |
| `ValueTuple` `Equals`, `GetHashCode`, `CompareTo`, `ToString` | not listed | Comparers by value | |

Every public member of `Nullable`:

| Member | What it does with a value | List entry |
|---|---|---|
| `Nullable.Compare<T>(T?, T?)` | compares the values | not listed: passes a value to a comparer |
| `Nullable.Equals<T>(T?, T?)` | compares the values | not listed: passes a value to a comparer |
| `Nullable.GetUnderlyingType(Type)` | no value | not listed: not generic |
| `Nullable.GetValueRefOrDefaultRef<T>(ref readonly T?)` | a basic member: see the summary table | listed |

## Opaque and Untyped Sinks

Members that do not reach into the elements.

| Member | Listed | Reason | Source |
|---|---|---|---|
| `Object.GetType()` | listed | Header only | CLR/Object.CoreCLR.cs:14 |
| `Object.ReferenceEquals` | listed, `borrows` | Reference comparison | CL/Object.cs:67 |
| `Object.Equals(object)`, `Equals(object, object)`, `GetHashCode()`, `ToString()` | not listed | Virtual: a container such as `Tuple<Guard, int>` compares, hashes or formats its items by value | CL/Object.cs:48-74 |
| `ArgumentNullException.ThrowIfNull(object, string)` | listed, `borrows` | Null check | CL/ArgumentNullException.cs:55 |
| `ObjectDisposedException.ThrowIf(bool, object)` | listed, `borrows` | Uses `instance?.GetType().FullName` | CL/ObjectDisposedException.cs:57; CL/ThrowHelper.cs:456-458 |
| `GC.KeepAlive` | listed, `borrows` | Nothing | CLR/GC.CoreCLR.cs:288 |
| `RuntimeHelpers.GetHashCode(object)` | listed, `borrows` | Identity hash | CLR/Runtime/CompilerServices/RuntimeHelpers.CoreCLR.cs:244 |
| `Monitor.Enter` ×2, `TryEnter` ×6, `Exit`, `IsEntered`, `Wait` ×3, `Pulse`, `PulseAll` | listed, `borrows` | Use the object's sync block only | CLR/Threading/Monitor.CoreCLR.cs:33, 154; CL/Threading/Monitor.cs:24 |

### Argument Checks

`ArgumentOutOfRangeException` holds the value as an object (`ActualValue` is boxed), and its `ThrowIf*` members compare the value. None of them is listed.

| Member | What it does with a value | List entry |
|---|---|---|
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException()` | holds the value as an object | not listed: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?)` | holds the value as an object | not listed: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?, Exception?)` | holds the value as an object | not listed: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?, object?, string?)` | holds the value as an object | not listed: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ArgumentOutOfRangeException(string?, string?)` | holds the value as an object | not listed: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.GetObjectData(SerializationInfo, StreamingContext)` | holds the value as an object | not listed: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.ThrowIfEqual<T>(T, T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual<T>(T, T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfGreaterThan<T>(T, T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfLessThanOrEqual<T>(T, T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfLessThan<T>(T, T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfNegativeOrZero<T>(T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfNegative<T>(T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfNotEqual<T>(T, T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ThrowIfZero<T>(T, string?)` | compares the value | not listed: passes a value to a comparison |
| `ArgumentOutOfRangeException.ActualValue` | holds the value as an object | not listed: not generic; ActualValue is boxed |
| `ArgumentOutOfRangeException.Message` | holds the value as an object | not listed: not generic; ActualValue is boxed |

## Non-copyable and View Types

### Non-copyable Types

Besides `SpinLock` and `GCHandle`, the list marks these structs `noncopyable` (eleven in all). Each owns something that a copy would release twice:

- `GCHandle<T>`, `PinnedGCHandle<T>` and `WeakGCHandle<T>` are .NET 10 types; their `Dispose` frees the handle (`CL/Runtime/InteropServices/GCHandle.T.cs:89`).
- `Lock.Scope`: `Dispose` exits the lock (`CL/Threading/Lock.cs:137-143`).
- `MemoryHandle`: `Dispose` frees a `GCHandle` and unpins (`CL/Buffers/MemoryHandle.cs:40-54`).
- `DefaultInterpolatedStringHandler`: `ToStringAndClear` returns its rented buffer to the pool (`CL/Runtime/CompilerServices/DefaultInterpolatedStringHandler.cs:110-136`).
- `AsyncFlowControl`: `Dispose`/`Undo` restores the flow (`CL/Threading/ExecutionContext.cs:550`).
- `ManualResetValueTaskSourceCore<TResult>`: a mutable struct holding `_result` and the continuation, meant to live in one field (`CL/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs:34,65`).
- `DispatcherProcessingDisabled` of WindowsBase: `Dispatcher.DisableProcessing` adds 1 to `_disableProcessingCount` and returns the struct with its `_dispatcher` set, and `Dispose` checks only that field of the struct before it subtracts 1 and clears it, so the `Dispose` of a copy subtracts a second time (dotnet/wpf `src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/Threading/DispatcherProcessingDisabled.cs` at `v10.0.0` and `v11.0.0`, the same; the IL of `Dispose` and `DisableProcessing` in the WindowsBase of the .NET Framework 4.8.1 does the same).

Checked and not marked:

Of the eleven, five are marked `unsafetocopy`, since a copy can break memory safety, and six stay `noncopyable`, since a copy only breaks the logic of the program. Read in the IL of `System.Private.CoreLib` of .NET 11.0.0, decompiled:

- `GCHandle.Free` exchanges the struct's own `_handle` with zero and frees the slot that the old value names (`InternalFree`); `GCHandle<T>.Dispose`, `PinnedGCHandle<T>.Dispose` and `WeakGCHandle<T>.Dispose` read `_handle`, set it to zero and free the slot. Nothing checks that the slot is still the one that was allocated, so the `Free` or `Dispose` of a copy frees a slot that the runtime may have given to another object since, or unpins memory that a pointer from `AddrOfPinnedObject` or `PinnedGCHandle<T>.GetAddressOfObjectData` still points into. `GCHandle<T>.Target` returns `Unsafe.As<T>(GCHandle.InternalGet(handle))`, so a copy that is read after its slot went to another object hands out that object as a `T`. All four are `unsafetocopy`.
- `MemoryHandle.Dispose` frees its `GCHandle` field where it is allocated and calls `Unpin` on its `IPinnable` field, so the `Dispose` of a copy frees and unpins a second time, while the `Pointer` of the other copy may still point into the memory. `unsafetocopy`.
- `DefaultInterpolatedStringHandler.Clear`, which `ToStringAndClear` calls, returns the `char[]` that the handler rented from `ArrayPool<char>.Shared`, so a copy returns the same array twice, and two later renters can share it. That corrupts data but reaches no invalid memory: the array is managed and kept alive by every reference to it, and the span of a copy points into it. It is the closest call of the list, and stays `noncopyable`.
- `SpinLock`, `Lock.Scope`, `AsyncFlowControl`, `DispatcherProcessingDisabled` and `ManualResetValueTaskSourceCore<T>`: a copy exits a lock, restores a flow or subtracts from a count twice, or holds a result and a continuation in two places. `noncopyable`.

- **Left copyable:** `ValueTask<T>`, `Nullable<T>`, `KeyValuePair`, `ValueTuple` and `List<T>.Enumerator`. These are generic structs that hold `T` inline, so they are already non-copyable over a non-copyable `T` (see [Non-copyable Types](../../Docs/articles/rules/non-copyable-types.md)), and they are not views.
- **Not marked `noncopyable`:**
  - `CancellationTokenRegistration`: copies dispose idempotently, by id.
  - `SpinWait`: a copy only loses the count.
  - `SequenceReader<T>` and `Utf8JsonReader`: copying them is the documented way to save state.
  - The method builder structs: compiler-owned.

### Views

A view holds a reference to storage and copies no elements when it is copied. Besides `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ReadOnlyMemory<T>` and `ArraySegment<T>`, the list marks these `view`:

- `Span<T>.Enumerator`, `ReadOnlySpan<T>.Enumerator`, `ArraySegment<T>.Enumerator`: these hold the span or array and an index.
- `ImmutableArray<T>` and its `Enumerator`: `internal readonly T[]? array`, at `Lib/System.Collections.Immutable/src/System/Collections/Immutable/ImmutableArray_1.Minimal.cs:54`.
- `ReadOnlySequence<T>`: two objects and two integers (`Lib/System.Memory/src/System/Buffers/ReadOnlySequence.cs:18-21`), and its `Enumerator`.
- `TaskAwaiter<T>`, `ConfiguredTaskAwaitable<T>` and its awaiter: these hold only a `Task<T>` reference (`CL/Runtime/CompilerServices/TaskAwaiter.cs:292,467`).
- The alternate lookups of `Dictionary<TKey, TValue>`, `ConcurrentDictionary<TKey, TValue>` and `FrozenDictionary<TKey, TValue>`: each looks up in the dictionary that it holds.

`ValueTaskAwaiter<T>` and the `ConfiguredValueTaskAwaitable<T>` types are **not** views: they hold a `ValueTask<T>`, which stores the result inline.

### Members of the Non-copyable Types That Take One by Value

Eighteen members of the built-in non-copyable structs take the struct by value and only read it, so they are marked `borrows`. They are read on dotnet/runtime `main`, 16 November 2026: `ToIntPtr` returns `value._handle` and `Equals` compares `_handle` in `GCHandle`, `GCHandle<T>`, `PinnedGCHandle<T>` and `WeakGCHandle<T>`; the `==`, `!=` and `IntPtr` conversion of `GCHandle` do the same; `GCHandleExtensions.GetAddressOfArrayData` and `GetAddressOfStringData` read `handle.Target` once; `AsyncFlowControl.Equals` compares `_thread`, and its operators call it. The `==` and `!=` of `DispatcherProcessingDisabled` call its `Equals(object)`, which compares `_dispatcher` (dotnet/wpf at `v10.0.0` and `v11.0.0`). None frees, stores or changes the value. `DispatcherProcessingDisabled.Equals(object)` has no line: its argument is a box, and a stored value converted to one is a copy.

Not marked `borrows`: `MemoryHandle..ctor(void*, GCHandle, IPinnable)` keeps the handle and frees it in `Dispose`, so a stored handle passed to it is a second owner. `JavaMarshal.GetContext(GCHandle)` is not marked either: the evidence has no reading of its source.

### Members That Get at a Built-in Non-copyable Type Other Than by Value

A member of an assembly that was not built with the analyzer and that takes a value of one of the built-in non-copyable types by `ref`, `in` or `ref readonly`, in a container, in a view or through a pointer needs a `borrows` entry, or its use gets a warning (see [Code Not Built With the Analyzer](../../Docs/articles/rules/unchecked-code.md)). `FrameworkScanTests` finds every such member of the shared frameworks. Each is read in the runtime source, and the same evidence is in the comments of the list:

| Member | What it does with the value | List entry |
|---|---|---|
| `String.Create(IFormatProvider, ref DefaultInterpolatedStringHandler)`, `String.Create(IFormatProvider, Span<char>, ref DefaultInterpolatedStringHandler)` | returns `handler.ToStringAndClear()` (String.cs 352-361 at d757c03) | `borrows` |
| `HybridCache.GetOrCreateAsync` (four overloads with a `ref DefaultInterpolatedStringHandler` key) | passes `key.Text` on and clears the key before it returns (HybridCache.cs 100-139 at d757c03; the two overloads with a `HybridCacheEntryContext` at v11.0.0, 283-320, through `ClearAndReturn`) | `borrows` |
| `JavaMarshal.FinishCrossReferenceProcessing(MarkCrossReferencesArgs*, ReadOnlySpan<GCHandle>)` | pins the span and passes it to the runtime, which destroys each handle in place and keeps nothing (JavaMarshal.cs 61-78, interoplibinterface_java.cpp 111-135 at d757c03). It copies no handle: it ends each one, as `GCHandle.Free` does | `borrows` |

## Who Keeps a Container

A parameter whose type mentions a type parameter that is not marked is taken to keep the container that it is given (see [What the Keeping Check Follows](../../Docs/articles/rules/what-keeping-follows.md#code-that-nothing-checks)), unless the member's entry is marked `borrows`. Every member of the list with such a parameter, 92 of them, is read in the runtime source at `d757c03be2c3` for this one question: does it keep what it is given, only use it during the call, or hand it back. The 7 handle members of the first group carry `borrows` for the reason given in [Members of the Non-copyable Types That Take One by Value](#members-of-the-non-copyable-types-that-take-one-by-value), and 16 other members carry it too; none needs a second marker.

| Member | Parameter | Use | Why |
|---|---|---|---|
| `M:System.Runtime.InteropServices.GCHandle`1.ToIntPtr(System.Runtime.InteropServices.GCHandle{`0})` | `GCHandle<T>` | only uses: `borrows` (handle members) | returns `value._handle` |
| `M:System.Runtime.InteropServices.GCHandle`1.Equals(System.Runtime.InteropServices.GCHandle{`0})` | `GCHandle<T>` | only uses: `borrows` (handle members) | compares `_handle` |
| `M:System.Runtime.InteropServices.PinnedGCHandle`1.ToIntPtr(System.Runtime.InteropServices.PinnedGCHandle{`0})` | `PinnedGCHandle<T>` | only uses: `borrows` (handle members) | returns `value._handle` |
| `M:System.Runtime.InteropServices.PinnedGCHandle`1.Equals(System.Runtime.InteropServices.PinnedGCHandle{`0})` | `PinnedGCHandle<T>` | only uses: `borrows` (handle members) | compares `_handle` |
| `M:System.Runtime.InteropServices.WeakGCHandle`1.ToIntPtr(System.Runtime.InteropServices.WeakGCHandle{`0})` | `WeakGCHandle<T>` | only uses: `borrows` (handle members) | returns `value._handle` |
| `M:System.Runtime.InteropServices.WeakGCHandle`1.Equals(System.Runtime.InteropServices.WeakGCHandle{`0})` | `WeakGCHandle<T>` | only uses: `borrows` (handle members) | compares `_handle` |
| `M:System.Runtime.InteropServices.GCHandleExtensions.GetAddressOfArrayData``1(System.Runtime.InteropServices.PinnedGCHandle{``0[]})` | `PinnedGCHandle<T[]>` | only uses: `borrows` (handle members) | reads `handle.Target` and returns a pointer to its data |
| `M:System.Threading.Tasks.Task.Run``1(System.Func{``0})` | `Func<T>` | keeps: no marker | `Task<TResult>.StartNew(null, function, ...)`: the delegate stays in the task until it has run |
| `M:System.Threading.Tasks.Task.Run``1(System.Func{``0},System.Threading.CancellationToken)` | `Func<T>` | keeps: no marker | `Task<TResult>.StartNew(null, function, ...)`: the delegate stays in the task until it has run |
| `M:System.Threading.Tasks.ValueTask`1.#ctor(System.Threading.Tasks.Task{`0})` | `Task<T>` | keeps: no marker | constructor: `_obj = task` |
| `M:System.Threading.Tasks.ValueTask`1.#ctor(System.Threading.Tasks.Sources.IValueTaskSource{`0},System.Int16)` | `IValueTaskSource<T>` | keeps: no marker | constructor: `_obj = source` |
| `M:System.Span`1.#ctor(`0[])` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept: a constructor keeps what it is given |
| `M:System.Span`1.#ctor(`0[],System.Int32,System.Int32)` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept: a constructor keeps what it is given |
| `M:System.Span`1.op_Equality(System.Span{`0},System.Span{`0})` | `Span<T> ; Span<T>` | only uses: `borrows` | compares `_length` and `_reference` |
| `M:System.Span`1.op_Inequality(System.Span{`0},System.Span{`0})` | `Span<T> ; Span<T>` | only uses: `borrows` | compares `_length` and `_reference` |
| `M:System.Span`1.op_Implicit(`0[])~System.Span{`0}` | `T[]` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.Span`1.op_Implicit(System.ArraySegment{`0})~System.Span{`0}` | `ArraySegment<T>` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.Span`1.op_Implicit(System.Span{`0})~System.ReadOnlySpan{`0}` | `Span<T>` | hands back, followed: no marker | makes a span over the same elements: a ref struct |
| `M:System.ReadOnlySpan`1.#ctor(`0[])` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept: a constructor keeps what it is given |
| `M:System.ReadOnlySpan`1.#ctor(`0[],System.Int32,System.Int32)` | `T[]` | keeps: no marker | constructor of a ref struct: `_reference = ref GetArrayDataReference(array)`; kept: a constructor keeps what it is given |
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
| `M:System.Buffers.ArrayPool`1.Return(`0[],System.Boolean)` | `T[]` | keeps: no marker | the pool stores the array for the next `Rent` (`SharedArrayPool<T>.Return`, `ConfigurableArrayPool<T>.Return`) |
| `M:System.Nullable`1.op_Explicit(System.Nullable{`0})~`0` | `Nullable<T>` | only uses: no marker | returns `value.Value`; `borrows` would also let a stored nullable of a non-copyable type be passed by value |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.Threading.Tasks.Task{``0}[])` | `Task<T>[]` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.Threading.Tasks.Task{``0},System.Threading.Tasks.Task{``0})` | `Task<T> ; Task<T>` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.Collections.Generic.IEnumerable{System.Threading.Tasks.Task{``0}})` | `IEnumerable<Task<T>>` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |
| `M:System.Threading.Tasks.Task.WhenAny``1(System.ReadOnlySpan{System.Threading.Tasks.Task{``0}})` | `ReadOnlySpan<Task<T>>` | hands back, followed: no marker | copies the collection and keeps the tasks in the returned task, whose result is one of them: followed as the container under another type |

- only uses: `borrows` (handle members): 7
- keeps: no marker: 15
- only uses: `borrows`: 16
- hands back, followed: no marker: 53
- only uses: no marker: 1
- total: 92

## Members That Receive a Value and Keep Nothing of It

A value that is converted to a type which hides it, as the argument of a member of the list, is let through only where the member is marked `borrows`. A line with no marker says how the member treats the values that it holds; it does not say that the member forgets a reference that it is given, and `List<T>.Add`, `Task.FromResult`, `TaskCompletionSource<T>.SetResult`, `ChannelWriter<T>.TryWrite`, `Interlocked.Exchange` and the `state` of a task keep theirs. Every member of the list with a parameter that can receive such a conversion (by value, `in` or `params`, of a type that is `object`, an interface, a class that is not sealed, or a type parameter) is read: 218, of which 32 keep nothing of what they are given and are marked `borrows`. The other 186 keep the argument, hand it back, hand out a view or a reference of it, or give it to code that the application can supply, and keep their line without `borrows`. The 30 key lookups of the dictionaries are among them: they give the key to the dictionary's comparer, which can keep it.

| Members | Why they keep nothing |
|---|---|
| `Unsafe.AreSame`, `IsAddressGreaterThan`, `IsAddressLessThan`, `IsAddressGreaterThanOrEqualTo`, `IsAddressLessThanOrEqualTo`, `ByteOffset`, `IsNullRef` (7) | They compare addresses or test one, and return a `bool` or an offset |
| `Array.Clear(Array)`, `Array.Clear(Array, int, int)` (2) | They clear the elements in place and return nothing |
| `Object.ReferenceEquals`, `ArgumentNullException.ThrowIfNull(object, string)`, `ObjectDisposedException.ThrowIf(bool, object)`, `GC.KeepAlive`, `RuntimeHelpers.GetHashCode(object)` (5) | They compare, test or read the identity of the object, or keep it alive until the call, and store nothing; `ThrowIf` takes the name of its type for the message |
| The 15 members of `Monitor`: `Enter`, `TryEnter`, `Exit`, `IsEntered`, `Wait`, `Pulse`, `PulseAll` | The lock is a state of the object's header, and no member hands the object to other code |
| `Task.WaitAll(Task[])`, `Task.WaitAll(IEnumerable<Task>, CancellationToken)`, `Task.WaitAny(Task[])` (3) | They wait for the tasks during the call; `WaitAny` returns an index. `WhenAll` and `WhenAny` keep the tasks in the task that they return, and keep their line with no marker |

`borrows` also lets a stored value of a non-copyable type be passed by value to these members, and counts a container that they are given as used: neither is new for them, since none of them keeps what it is given.

## Members That Keep Nothing of a Value in Some Outcomes

`borrowswhen=false` says that a member keeps nothing of a value that it is given by value where it returned false, and has taken it where it returned true or threw. The caller still holds the value where the member returned false. Each member that has it returns false before it keeps anything of the item, which was read at the release tags `v10.0.0` and `v11.0.0`.

| Member | Where it takes the value | Where it returns false | Source |
|---|---|---|---|
| `TaskCompletionSource<TResult>.TrySetResult(TResult)` | `Task<TResult>.TrySetResult` reserves the completion and stores `m_result` | Where the task was completed already, with nothing stored | TaskCompletionSource_T.cs:216-225, Future.cs (Task_T.cs in .NET 11):357-390 |
| `Dictionary<TKey, TValue>.TryAdd(TKey, TValue)` | `TryInsert(..., InsertionBehavior.None)`: `entry.value = value` | For a key that is there, before the store | Dictionary.cs:506-630 |
| `Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryAdd(TAlternateKey, TValue)` | `GetValueRefOrAddDefault`, then `slot = value` for a slot that it added | For a key that is there, before the store | Dictionary.cs:947-1020 |
| `ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue)` and the lookup's `TryAdd` | `TryAddInternal` (or its twin for an alternate key) makes the node with the value | For a key that is there, with nothing stored | ConcurrentDictionary.cs:367-375, 921-1050, 1941-2070, 2376-2490 |
| `BlockingCollection<T>.TryAdd(T)`, `(T, TimeSpan)`, `(T, int)`, `(T, int, CancellationToken)` | `TryAddWithNoTimeValidation`: `_collection.TryAdd(item)` into the collection's own `ConcurrentQueue<T>` | Where no room came up in time, before the item goes in | BlockingCollection.cs:300-470 |
| `ChannelWriter<T>.TryWrite(T)` | The buffer, a reader that is waiting, or the callback for dropped items | Where the channel is done writing, or a bounded one is full in the mode `Wait`, before it does anything with the item | BoundedChannel.cs:363-465, RendezvousChannel.cs:303-334, UnboundedChannel.cs, SingleConsumerUnboundedChannel.cs and UnboundedPriorityChannel.cs, at the `TryWrite` of each |

The row of `TryWrite` covers each of the five writers of the framework's channels: bounded, rendezvous, unbounded, unbounded for a single consumer and unbounded by priority. No other class of the framework derives from `ChannelWriter<T>`. `TryWrite` is abstract, so for a writer of another library the claim holds as far as that writer does what its documentation says: it returns "true if the item was written; otherwise, false if it wasn't written" (ChannelWriter.cs:22-25), as `ReadAllAsync` relies on its documentation above.

The other members of the list that return `bool` and take a value by value that is not a key are the ones in the table above.

**A note for a maintainer.** A member that throws may have taken the value, and some members of the list do throw after they have taken it. `TryWrite` and `WriteAsync` of a channel are two: the callback for dropped items of a bounded or rendezvous channel runs outside the lock and lets its exception through, and a reader that is completed with the item runs its continuation at once where the channel allows synchronous continuations, with no `try` around it (BoundedChannel.cs:363-455, 515-620; RendezvousChannel.cs:303-330; AsyncOperation.cs:190-285).

## Static Fields of Generic Types

Reading or writing a static field of a generic type runs the static constructor of the type with the type arguments of the use, and with it the initializers of its static fields. An `F:` line says that this code makes no copy of a value of the type arguments. `FrameworkScanTests` finds every public or protected static field, other than a constant, of a type that is generic or nested in a generic type, in the shared frameworks of .NET 10 and .NET 11 and in the .NET Framework 4.8.1, and fails for one with no line. It finds the same 11 fields in 10.0.14 and 11.0.0, and one in the .NET Framework 4.8.1, `XmlQuerySequence<T>.Empty`, which .NET has as well. Every type parameter counts, also one that is constrained to a class, since a container can be bound to it: that adds `EndpointMetadataComparer<TMetadata>.Default`.

**How it was read.** The source of each type, on the branch `release/10.0` of dotnet/runtime and of dotnet/aspnetcore (read 25 November 2026), and the IL of its static constructor in the implementation assemblies of 10.0.14 and 11.0.0, which is the same in both, and of 4.8.9363 for `System.Data.SqlXml`. The IL lists every static field of the type and every initializer, also those of other partial files. A type that the static constructor reaches whose own static constructor then runs with the same type arguments is read too.

| Field | What the static constructor does | What it reaches | Source |
|---|---|---|---|
| `ImmutableArray<T>.Empty` | `new ImmutableArray<T>(new T[0])`: an empty array | Nothing else | ImmutableArray_1.Minimal.cs:44 |
| `ImmutableDictionary<TKey, TValue>.Empty` | `new ImmutableDictionary<TKey, TValue>()`, and the delegate `s_FreezeBucketAction` | `Comparers.Get` with `EqualityComparer<TKey>.Default` and `EqualityComparer<TValue>.Default`, whose `Comparers.Default` holds the two comparers; `SortedInt32KeyNode<HashBucket>.EmptyNode`, made by a constructor that only sets `_frozen` | ImmutableDictionary_2.cs:25-71, ImmutableDictionary_2.Comparers.cs:151-160, SortedInt32KeyNode.cs:27, 66-69 |
| `ImmutableHashSet<T>.Empty` | `new ImmutableHashSet<T>(SortedInt32KeyNode<HashBucket>.EmptyNode, EqualityComparer<T>.Default, 0)`, and the delegate `s_FreezeBucketAction` | `Freeze` of the empty node, which is frozen already and does nothing; `GetHashBucketEqualityComparer`, which hands out a comparer of buckets made from the comparer of `T` | ImmutableHashSet_1.cs:27-79, 991-1005, SortedInt32KeyNode.cs:244-250 |
| `ImmutableList<T>.Empty` | `new ImmutableList<T>()`, which takes `Node.EmptyNode` | `Node.EmptyNode`, made by a constructor that only sets `_frozen` | ImmutableList_1.cs:24, 34, ImmutableList_1.Node.cs:22, 68-72 |
| `ImmutableSortedDictionary<TKey, TValue>.Empty` | `new ImmutableSortedDictionary<TKey, TValue>(null, null)`: `Comparer<TKey>.Default`, `EqualityComparer<TValue>.Default` and `Node.EmptyNode` | `Node.EmptyNode`, as above | ImmutableSortedDictionary_2.cs:24, 51-56, ImmutableSortedDictionary_2.Node.cs:22, 62-66 |
| `ImmutableSortedSet<T>.Empty` | `new ImmutableSortedSet<T>(null)`: `Node.EmptyNode` and `Comparer<T>.Default` | `Node.EmptyNode`, as above | ImmutableSortedSet_1.cs:39, 55-59, ImmutableSortedSet_1.Node.cs:21, 67-71 |
| `ReadOnlySequence<T>.Empty` | `new ReadOnlySequence<T>(Array.Empty<T>())`, which keeps the empty array as its start and its end | `Array.Empty<T>()`, an empty array | ReadOnlySequence.cs:26, 113-122 |
| `XmlQuerySequence<T>.Empty` | `new XmlQuerySequence<T>()`: `new T[16]`, an array of default values; on the .NET Framework also `typeof(XPathItem)` into a second static field | Nothing else | Lib/System.Private.Xml/src/System/Xml/Xsl/Runtime/XmlQuerySequence.cs:24, 68-71, and the IL of 4.8.1 |
| `NullLogger<T>.Instance` | `new NullLogger<T>()`, whose constructor only calls that of `object` | Nothing else | Lib/Microsoft.Extensions.Logging.Abstractions/src/NullLoggerT.cs:17 |
| `EventCallback<TValue>.Empty` | `new EventCallback<TValue>(null, (Action)(() => { }))`, which stores the two references | The class of the lambda's cache, whose static constructor makes its one instance | aspnetcore src/Components/Components/src/EventCallbackOfT.cs:16 |
| `FeatureReference<T>.Default` | `new FeatureReference<T>(default(T), -1)`: a default `T`, made in a local by `initobj` and moved into the field `_feature`, the one use of the local | Nothing else | aspnetcore src/Extensions/Features/src/FeatureReference.cs:15-24 |
| `EndpointMetadataComparer<TMetadata>.Default` | `new DefaultComparer<TMetadata>()`, whose constructor only calls that of its base class | Nothing else | aspnetcore src/Http/Routing/src/Matching/EndpointMetadataComparer.cs:81 |

`EqualityComparer<T>.Default` and `Comparer<T>.Default` make a comparer for the type with reflection and hold no value of it. So no static constructor above copies a value of its type arguments: what it makes is an empty collection, an empty array, a default value that is moved once, or an object that holds no such value.

`XmlQuerySequence<T>` is public in the implementation assembly `System.Private.Xml` of .NET, and no reference assembly of .NET has it, so no project for .NET can name it. Its line is marked `# netfx`: it resolves in the reference assembly `System.Data.SqlXml` of the .NET Framework 4.8.1, and the scan of .NET is answered by the same line.

## Classes That Clone Themselves

The `clones` lines name the classes of the framework whose own code clones an object with `MemberwiseClone`, so that an object of a class that derives from them is copied field by field. They come from a scan of the IL of the shared frameworks, not from a reading of the source: every assembly of Microsoft.NETCore.App, Microsoft.AspNetCore.App and Microsoft.WindowsDesktop.App, 10.0.14 and 11.0.0, every method body, every `call`, `callvirt`, `ldftn` and `ldvirtftn` of `Object.MemberwiseClone()` and `MarshalByRefObject.MemberwiseClone(bool)`, with the receiver read from the instruction before the call. `FrameworkScanTests` runs the same scan and fails for a class that it finds and that no line covers.

**What the scan finds.** 55 calls in about 40,000 types, the same in both versions. 49 are on `this`, 6 on another object, none on a receiver that the IL does not show. 22 of the classes that clone `this` can be derived from in another assembly (the definition of SCA1008, with no `InternalsVisibleTo`, since the framework's grants never reach an application). Two of them have no line, on purpose:

- **`System.Delegate`**: C# does not allow a class to derive from it (CS0644), and every delegate type is sealed.
- **`System.MarshalByRefObject`**: the call is the body of `MemberwiseClone(bool)` itself, which clones only when a derived class calls it. A call of it in a project gets SCA1005 there, and no class of the framework calls it.

The other 20 are covered by the 11 lines, since a line covers every class that derives from the class it names: the nine XmlSchema classes that clone themselves derive from `XmlSchemaObject`. Nine of the 11 lines say `clones=complete`. The two Xml lines say `clones`, because the members of the Xml classes are not analyzed (see below).

The last two columns come from the same scan, of 11.0.0: the framework methods that call the clone directly (a call through `ICloneable.Clone` is not counted), and the framework classes that derive from the listed class and can be derived from in turn.

| Line | Assembly | What clones | Framework methods that call it | Derivable framework classes that inherit it |
|---|---|---|---|---|
| `T:System.Text.Encoding;clones=complete` | System.Private.CoreLib | `Clone()`, public, virtual, the `ICloneable` implementation | `Console.InputEncoding` and `Console.OutputEncoding` (the setters), `Encoding.GetEncoding` and `EncodingProvider.GetEncoding` (the overloads with fallbacks), `TextWriterTraceListener`, the constructor of `XmlEncodedRawTextWriter` | 4: ASCIIEncoding, UTF7Encoding, UTF8Encoding, UnicodeEncoding |
| `T:System.Globalization.CultureInfo;clones=complete` | System.Private.CoreLib | `Clone()`, public, virtual; `ReadOnly(CultureInfo)`, public, static, which clones its argument | `Clone`: `X509Certificate.FormatDate` and two internal WPF helpers. `ReadOnly`: `RequestLocalizationMiddleware` of ASP.NET Core, the constructor of `MaskedTextProvider`, the WPF Ribbon | 0 |
| `T:System.Globalization.Calendar;clones=complete` | System.Private.CoreLib | `Clone()`, public, virtual; `ReadOnly(Calendar)`, public, static, which clones its argument | `CultureInfo.Clone`, `DateTimeFormatInfo.Clone`, `CultureInfo.ReadOnly`, `DateTimeFormatInfo.ReadOnly` | 15: GregorianCalendar, JapaneseCalendar, HebrewCalendar and the other calendars |
| `T:System.Xml.Schema.XmlSchemaObject;clones` | System.Private.Xml | `Clone()`, internal, virtual, and the overrides of nine derived classes, each of which clones `this` too | `XmlSchema.DeepClone`, the schema compiler (`SchemaCollectionCompiler.CanonicalizeElement`), the overrides themselves | 57: every public XmlSchema class |
| `T:System.Xml.XmlQualifiedName;clones` | System.Private.Xml | `Clone()`, internal | The `Clone` overrides of the XmlSchema classes | 0 |
| `T:System.Windows.Ink.Stroke;clones=complete` | PresentationCore | `Clone()`, public, virtual | `Stroke.Copy`, `Stroke.Clip`, `Stroke.Erase`, `StrokeCollection.Clone` | 0 |
| `T:System.Windows.Ink.DrawingAttributes;clones=complete` | PresentationCore | `Clone()`, public, virtual | `Stroke.Clone`, `Stroke.DrawCore`, the stroke renderers, `InkCanvas` and the ink collection behavior | 0 |
| `T:System.Drawing.Printing.PageSettings;clones=complete` | System.Drawing.Common | `Clone()`, public, virtual | `PrintController` (the print loops) | 0 |
| `T:System.Drawing.Printing.PrinterSettings;clones=complete` | System.Drawing.Common | `Clone()`, public, virtual | None in the framework: only code that calls it | 0 |
| `T:System.Drawing.Printing.Margins;clones=complete` | System.Drawing.Common | `Clone()`, public, virtual | `PageSettings.Clone` | 0 |
| `T:System.Windows.Forms.GiveFeedbackEventArgs;clones=complete` | System.Windows.Forms | `Clone()`, internal | `DropSource.GiveFeedback` (drag and drop) | 0 |

The 19 framework classes that derive from these and have `clones=complete` lines of their own are the four encodings (ASCIIEncoding, UTF7Encoding, UTF8Encoding and UnicodeEncoding) and the 15 public calendars (ChineseLunisolarCalendar, EastAsianLunisolarCalendar, GregorianCalendar, HebrewCalendar, HijriCalendar, JapaneseCalendar, JapaneseLunisolarCalendar, JulianCalendar, KoreanCalendar, KoreanLunisolarCalendar, PersianCalendar, TaiwanCalendar, TaiwanLunisolarCalendar, ThaiBuddhistCalendar and UmAlQuraCalendar).

**The other receivers.** `DateTimeFormatInfo.ReadOnly`, `NumberFormatInfo.ReadOnly` and `TextInfo.ReadOnly` clone their argument as `Calendar.ReadOnly` and `CultureInfo.ReadOnly` do, but the three classes are sealed. `RuntimeHelpers.GetObjectValue` clones a boxed struct, and gets SCA1007 where a project calls it. The 27 classes that clone `this` and cannot be derived from are sealed or not public: the enumerators of the non-generic collections, `Array`, NumberFormatInfo, DateTimeFormatInfo, TextInfo, XmlReaderSettings, XmlWriterSettings, the Qil nodes of the XSLT compiler and a few internal WPF types.

**A class that replaces fields after the clone is listed all the same.** CultureInfo clones its Calendar, Stroke its DrawingAttributes and PageSettings its Margins, as the callers above show. That fixes the class's own fields, not those that a derived class declares, which the clone copies whatever the base class does afterwards.

**The methods that clone have lines of their own.** Each method of a listed class that the scan finds cloning and that another assembly can call (public, protected, or protected internal) has an `M:...;clones` line: the `Clone` methods of Encoding, CultureInfo, Calendar, Stroke, DrawingAttributes, PageSettings, PrinterSettings and Margins, and the static `ReadOnly` methods of CultureInfo and Calendar, 10 lines. A symbol list line can only say that a member makes no copy, so a use of one of these that the analyzer cannot vouch for gets no advice to list it, and a line that says one makes no copy is left out with SCA1042. The internal `Clone` methods of the XmlSchema classes, XmlQualifiedName and GiveFeedbackEventArgs cannot be called from a project and need none. `FrameworkScanTests` fails for a method that clones and that another assembly can call but has no line.

**The members that clone through other members.** A member with no line of a class whose line says `clones=complete` is taken not to clone its object, so the scan follows the calls. In the IL of every method from which a call of MemberwiseClone can be reached, it follows which argument each value on the stack may be, over every path, and works out the arguments that each method may clone and those that it may return, to a fixed point: a method clones an argument that it gives to an argument of a method that clones that one. A virtual call on the method's own object reaches the overrides of its class's family, one on another value those of every class that derives from the called method's class. What a method returns is followed for the methods of the classes whose chain reaches a class that clones itself, where a member can hand back its object for another one to clone; a method of any other class counts as returning none of its arguments. Of 357,403 methods of 11.0.0 it follows 52,250, in about 20 seconds, and finds 35 that clone an argument. Besides the lines above, 7 are public or protected:

| Member | What it clones |
|---|---|
| `Stroke.GetClipResult(IEnumerable<Point>)`, `GetClipResult(Rect)` | The stroke, through the internal `Clip` and `Copy` |
| `Stroke.GetEraseResult(IEnumerable<Point>)`, `GetEraseResult(IEnumerable<Point>, StylusShape)`, `GetEraseResult(Rect)` | The stroke, through the internal `Erase` and `Copy` |
| `Stroke.Draw(DrawingContext, DrawingAttributes)`, `Stroke.DrawCore(DrawingContext, DrawingAttributes)` | The drawing attributes that it is given, through the internal `DrawInternal` |

The other 28 are internal: `Stroke.Clip`, `Copy`, `Erase` and `DrawInternal`, `GiveFeedbackEventArgs.Clone`, `XmlQualifiedName.Clone`, and the `Clone` and `CloneParticle` methods of the XmlSchema classes. So the XmlSchema classes, XmlQualifiedName and GiveFeedbackEventArgs have no member line.

**The Xml classes.** The members of the Xml classes are not analyzed, so `XmlSchemaObject` and `XmlQualifiedName` are not marked `clones=complete`, and every member of them and of the XmlSchema classes counts as one that can clone. A symbol list file can mark them, where its author vouches for their members: a `clones=complete` line counts wherever it stands, and a plain `clones` line takes nothing away. GiveFeedbackEventArgs is marked `clones=complete`: see [Members Whose Own Object Reaches Other Code](#members-whose-own-object-reaches-other-code).

**The .NET Framework 4.8.1.** The same scans read its implementation assemblies: every assembly that `RedistList/FrameworkList.xml` of its reference assemblies names, from the global assembly cache or the folder of the runtime, 235 of them (the list also names System.Activities.Statements, which exists neither as a reference assembly nor in the runtime). They find the classes above, and 7 classes that only the .NET Framework has, can be derived from in another assembly, and clone `this` with `MemberwiseClone`. Their lines end in `# netfx`:

| Line | Assembly | What clones |
|---|---|---|
| `T:System.Messaging.MessagePropertyFilter;clones` | System.Messaging | `Clone()`, public, virtual |
| `T:System.Workflow.Activities.Rules.Rule;clones` | System.Workflow.Activities | `Clone()`, public, which clones its condition and its actions as well |
| `T:System.Workflow.Activities.Rules.RuleSet;clones` | System.Workflow.Activities | `Clone()`, public, which clones its rules as well |
| `T:System.Workflow.Activities.Rules.RuleHaltAction;clones`, `RuleStatementAction`, `RuleUpdateAction` | System.Workflow.Activities | `Clone()`, public, the override of the abstract `RuleAction.Clone` |
| `T:Microsoft.VisualBasic.Activities.VisualBasicImportReference;clones` | System.Activities | `Clone()`, internal |

The six public `Clone` methods have `M:...;clones` lines of their own, and the members scan finds no other public or protected member of these classes that clones. Their members were not analyzed for what reaches other code, so the lines say `clones`, not `clones=complete`, and every member of them that no line names counts as one that can clone. Their assemblies are not in FrameworkAssemblies.txt: the analyzer takes them for other libraries, whose classes it knows nothing of, so a class of a project that derives from one of these got a warning before as well, with the text of an unknown base class. The scan also finds no member of the .NET Framework that gets at one of its three built-in non-copyable types, `SpinLock`, `GCHandle` and `AsyncFlowControl`, other than by value and has no entry.

**The members that can hand back another object of their class.** A member of these classes whose result, or `out` or `ref` parameter, the object converts to by reference (the class, a class above it, `object`, an interface that it implements, or a type parameter) could hand back the object itself for another member to clone, and gets the warning unless a line says that it does not. Besides the `Clone` methods, which have their `clones` lines, 5 such members of the 8 classes with member lines and of the framework classes that derive from them have a line with no marker, and the scan checks that no path of their IL returns argument 0:

| Line | What it hands back |
|---|---|
| `P:System.Globalization.CultureInfo.Parent` | The parent culture: `InvariantCulture`, or a culture of the cache for the parent's name, never the culture itself |
| `M:System.Globalization.CultureInfo.GetFormat(System.Type)` | `NumberFormat`, `DateTimeFormat` or null |
| `M:System.Globalization.CultureInfo.GetConsoleFallbackUICulture` | A new specific culture for the console's fallback name, kept for the next call |
| `M:System.Windows.Ink.Stroke.GetPropertyData(System.Guid)` | The object that `AddPropertyData` was given for the Guid. Giving it the stroke itself is a conversion to `object` for a member that keeps it, which gets the warning there |
| `M:System.Windows.Ink.DrawingAttributes.GetPropertyData(System.Guid)` | The same, for the drawing attributes |

## Members Whose Own Object Reaches Other Code

A member line of a class that clones itself says what the member does with its object: a `clones` line says that it clones the
object, or the object that it is given, or hands it to code that can. Where the class's line says `clones=complete`, a member with no
line is taken to do neither. That is the list's statement, and for the built-in list the scan backs it. In the IL of the members of
the classes whose line says `clones=complete` (Encoding, CultureInfo, Calendar, Stroke, DrawingAttributes, PageSettings,
PrinterSettings, Margins, GiveFeedbackEventArgs and the framework classes that derive from them), FrameworkScanTests follows where the object that a member is called on can go, through the methods that it calls, to a fixed
point. The object reaches other code where it is stored in a field, a static, an element or through a reference; given to a method
with no body to read, a delegate's `Invoke` and the constructor of a delegate that captures it included; or given, other than as the
object that the call is made on, to a virtual method that an application can override. The members of the Xml classes are not
analyzed: `XmlSchemaObject` and `XmlQualifiedName` are not marked `clones=complete`, every member of them and of the XmlSchema classes
counts as one that can clone, and the scan leaves them out. GiveFeedbackEventArgs goes through the same scans: its
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
taken to keep nothing of the object: those of `Monitor`, `RuntimeHelpers.GetHashCode` and `TryGetHashCode`, and `MemberwiseClone` on the
.NET Framework (see below). Constructors are not
members that an application calls on an existing object, but one can store the new object in another object that a member hands out
afterwards, so the scan follows them too.

Of 11.0.0 and of 10.0.14, the scan flags the same 18 public or protected members and the constructors of three classes. One
constructor leads to a line; the others hand nothing on, for the reasons below:

| Members | What the scan sees, and why it does or does not hand the object on |
|---|---|
| `new PrinterSettings()` | The constructor stores the printer settings in the `PageSettings` that it makes for `DefaultPageSettings`, and `PageSettings.PrinterSettings` hands them back. So `P:System.Drawing.Printing.PrinterSettings.DefaultPageSettings` has a `clones` line: it hands the object to code that can clone it |
| `Stroke.AddPropertyData`, `Stroke.RemovePropertyData`, `Stroke.Transform`, `Stroke.OnDrawingAttributesChanged`, `Stroke.OnDrawingAttributesReplaced`, `Stroke.OnInvalidated`, `Stroke.OnPropertyChanged`, `Stroke.OnPropertyDataChanged`, `Stroke.OnStylusPointsChanged`, `Stroke.OnStylusPointsReplaced`, `DrawingAttributes.OnAttributeChanged`, `DrawingAttributes.OnPropertyChanged`, `DrawingAttributes.OnPropertyDataChanged` | They raise an event of the object with the object as the sender. A handler gets it only where it was subscribed: through the object, which warns as a member whose handler type takes `object` (SCA1009), through a `Stroke`, a `DrawingAttributes` or an `INotifyPropertyChanged` that the object was converted to, which warns there, or by code of the object's own class, which is judged on its own |
| `new Stroke()`, `new DrawingAttributes()`, `Stroke.DrawingAttributes`, `Stroke.StylusPoints` | They subscribe a handler of the object's own to an event of its drawing attributes, its stylus points or its extended properties: the delegate holds the object as its target, and the event of the other object runs only that handler of the object's own class. No member hands out the invocation list of those events |
| `PageSettings.HardMarginX`, `PageSettings.HardMarginY`, `PageSettings.PrintableArea` | They give the page settings to `PrinterSettings.CreateDeviceContext(PageSettings)` or `CreateInformationContext(PageSettings)`, which copy them into a native DEVMODE and keep nothing. The scan tells the overloads of a method apart by their number of parameters only, and sees the overload with an `HGLOBAL` hand its argument to `GlobalLock` |

### The .NET Framework 4.8.1

The scan of the .NET Framework 4.8.1, of its implementation assemblies of file versions 4.8.9221 to 4.8.9363, flags the members above and 14 more, 9 of `PrinterSettings` (14 with their overloads) and 5 of `PageSettings`. None of them has a line. A line applies to its ID on every framework, so a line for one of them would add warnings on .NET, where none of them hands its object on, and one of them is `ToString`, which no `clones` line may name: the code that the compiler writes for a record calls it, and is not checked for that reason (see DESIGN.md). What the scan sees, and what was judged:

| Members | What the scan sees on the .NET Framework 4.8.1, and why it is not a hand-on that matters |
|---|---|
| `PrinterSettings.Collate`, `PrinterSettings.Copies`, `PrinterSettings.Duplex`, `PrinterSettings.IsPlotter`, `PrinterSettings.SupportsColor`, `PrinterSettings.CreateMeasurementGraphics` (four overloads), `PrinterSettings.GetHdevmode` (two), `PrinterSettings.IsDirectPrintingSupported` (two), `PrinterSettings.ToString` | Each reaches the internal `GetHdevmodeInternal`, directly or through `GetModeField`, `CreateDeviceContext` or `CreateInformationContext`, and that throws `new InvalidPrinterException(this)` where the printer name is not valid. The exception keeps the printer settings in a private field, and they get out of it only through its `GetObjectData`, which adds them to the `SerializationInfo` of a serialization of the exception. That is judged not to be a hand-on that matters: no member of the exception hands them to code, and what a formatter does with them is what it does with any serializable object that it is given; `PrinterSettings` is serializable on its own. A round trip through a formatter does make a new object, for a derived class that is serializable too. On .NET the exception keeps nothing |
| `PageSettings.Bounds`, `PageSettings.CopyToHdevmode`, `PageSettings.PaperSource`, `PageSettings.PrinterResolution`, `PageSettings.ToString` | They give `new HandleRef(this, handle)` to native methods, and the scan sees the constructor of `HandleRef` store the object in its field. A `HandleRef` keeps nothing: it is a struct that the call is given by value, and it keeps the object alive until the call returns. The members of `PrinterSettings` above make one as well |

The scan of the .NET Framework would also flag every `Clone` of the classes whose line says `clones=complete` and the members of `Stroke` that clone through it, 18 rows, each a member with a `clones` line: `Object.MemberwiseClone` has no body there, and the scan takes a method with no body to keep its arguments. It is taken to keep nothing, as `RuntimeHelpers.GetHashCode` is: it copies the object into a new one, and the scan of what a member clones finds the clone by the call. On .NET it has a body, which the scan reads.

## Entry Validation

- **Every entry names exactly one public symbol of the framework.** The test `BuiltInList_ParsesAndEveryEntryNamesOnePublicSymbolOfTheFramework` of `SafeListTests` reads the list as a symbol list file, so that a line that cannot be parsed or a marker that does not fit its member fails, and resolves each entry with `DocumentationCommentId.GetSymbolsForDeclarationId` against the reference assemblies of .NET 10 and .NET 11, with ASP.NET Core and the Windows desktop pack. A `view` or `noncopyable` marker sits on a struct, and no entry is there twice.
- **On an older framework some entries name nothing,** and an entry that names nothing is ignored. Such entries stay on the list, marked with a trailing comment.
- **Entries that do not resolve on .NET 8,** among them:
  - .NET 10 or newer: `GCHandle<T>`, `PinnedGCHandle<T>`, `WeakGCHandle<T>`, `ImmutableCollectionsMarshal.AsMemory(ImmutableArray<T>.Builder)`.
  - .NET 9 or newer: `Lock.Scope`, the two `CollectionsMarshal` overloads taking `Dictionary<,>.AlternateLookup<>`, `Task.WhenAll(ReadOnlySpan<Task>)`, `Task.WhenAny(ReadOnlySpan<Task>)`, `Task.WaitAll(ReadOnlySpan<Task>)`, `Task.WaitAll(IEnumerable<Task>, CancellationToken)`.
- **Parser note:** `#` may only start a comment at the start of a line or after white space. A plain "`#` starts a comment" rule would cut every `#ctor` entry.
