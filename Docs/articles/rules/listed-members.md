<div class="article">

# What a Member on the Symbol List Hands Out

A member of an unmarked generic that is on the [symbol list](symbol-list-format.md) may be used with a non-copyable type. This page says what such a member hands out: a new value, or a copy that gets a warning.

### At a Glance

A member on the [symbol list](symbol-list-format.md) hands out a value that is **as new as the object that it was called on and the arguments that it was called with**:

| Called on | What it hands out by value |
|---|---|
| A new value, such as the task that a call has just returned | A new value |
| A stored container, such as a list in a field or a task that is used again | A copy, with a warning (SCA1030) |
| A view, such as an <xref:System.ArraySegment`1> | Always a copy: a view never owns its elements |

Two kinds of member are exceptions. A member that takes the value out for good hands out a new value whatever it is called on, and so does the task of a completion source in the method that made it.

## Examples

```cs
LockGuard a = await myLock.EnterAsync();                       // OK: read from a new value
LockGuard b = await myLock.EnterAsync().ConfigureAwait(false); // OK

Task<LockGuard> stored = EnterAsync();
LockGuard c = await stored;                                    // SCA1030: the task still holds the guard
bool done = stored.IsCompleted;                                // (the task is used again)
LockGuard d = list[0];                                         // SCA1030: the list still holds the guard

foreach (LockGuard item in CreateGuards()) { }                 // OK: the list is a new value
foreach (LockGuard item in list) { }                           // SCA1030

ref LockGuard r = ref span[1];                                 // OK: a reference
span[1].Dispose();                                             // OK: called in place
foreach (ref LockGuard item in span) { }                       // OK
```

## Views

A view, such as an <xref:System.ArraySegment`1>, never owns its elements. An element that its indexer or its enumerator hands out by value is a copy, whatever the view came from:

```cs
LockGuard e = new ArraySegment<LockGuard>(CreateArray())[0];   // SCA1030: the array holds the guard
```

## Members That Take a Value Out

The [built-in framework symbol list](framework-symbol-list.md) has the members that take the value out for good, such as <xref:System.Collections.Generic.Stack`1.Pop*?displayProperty=nameWithType> and <xref:System.Collections.Generic.Queue`1.Dequeue*?displayProperty=nameWithType>, as handing out a new value whatever they are called on. That applies to a value of a non-copyable type: taking it out leaves no second instance behind.

A container that such a member hands out, as from a `Stack<Task<LockGuard>>`, is a reference that whoever put it in may still hold. So it is as new as the object and the arguments of the call, like the result of any other member on the [symbol list](symbol-list-format.md).

## Waiting for Several Tasks

<xref:System.Threading.Tasks.Task.WhenAny*?displayProperty=nameWithType> over tasks of a guard hands out one of the tasks that it was given, so awaiting that task is a read from a stored one. The generic <xref:System.Threading.Tasks.Task.WhenAll*?displayProperty=nameWithType> copies the results into an array and is not on the [built-in framework symbol list](framework-symbol-list.md).

To wait for several tasks without copying the results, cast one of them to `Task`, which picks the overload of `WhenAll` that returns no results. But that call passes the tasks on, and a task that was passed to anything is no longer the method's own. A result that is read from it afterwards gets a warning (SCA1030), as it does after a `WhenAny` with a delay:

```cs
await Task.WhenAll((Task)first, second);      // the tasks are passed on here...
LockGuard g = await first;                    // SCA1030: ...so 'first' is no longer the method's own
```

Two forms stay clean:

```cs
var first = a.EnterAsync();
var second = b.EnterAsync();
using var g1 = await first;                              // OK: both tasks run already, so awaiting them one after
using var g2 = await second;                             //     the other waits no longer than WhenAll does

var pending = myLock.EnterAsync();
using var held = await pending.WaitAsync(timeout);       // OK: no copy, since 'pending' is not used again
```

The second form gets no warning, but it is not how to limit a wait for a lock: see [Time Limit on a Wait](../guides/tasks.md#time-limit-on-a-wait).

## Tasks in a Variable

A task or a value task in a variable is a stored value up to the [last use](last-use.md) of the variable. So a read of its result gets a warning if the method uses the variable again afterwards. "Start now, await later" needs nothing more:

```cs
var pending = myLock.EnterAsync();                       // start now
using var held = await pending;                          // await later: OK, 'pending' is not used again
```

A value task in a field, or in a variable that is used again afterwards, is read by moving it out:

```cs
using var held = await NonCopyable.Move(ref _pending);   // OK: a value task is moved out of the field
```

## Classes That Derive From a Container on the Symbol List

**A class that derives from a container on the [symbol list](symbol-list-format.md) is never a new container for the members that it inherits.** With `class Guards : List<LockGuard>`, the indexer of <xref:System.Collections.Generic.List`1> hands out what the object holds "as new as the object". Nothing checks the code of `Guards` against that: it can keep `this`, hand it out, or fill itself from anywhere.

So a by-value read through an inherited member is a read from a stored container (SCA1030) whatever the object was created from, even at the [last use](last-use.md) of a local that the method owns. And the object is stored where it is converted to the container that it derives from or to an interface of it:

```cs
sealed class Guards : List<LockGuard>
{
    private static readonly List<Guards> s_all = new();
    public void Register() => s_all.Add(this);        // nothing in the code of a caller shows this
}

var guards = new Guards();
guards.Add(myLock.Enter());                           // OK: used in place
CollectionsMarshal.AsSpan(guards)[0].Dispose();       // OK: used in place
LockGuard a = guards[0];                              // SCA1030, even where 'guards' is not used again
foreach (LockGuard g in MakeGuards()) { }             // SCA1030: a Guards from a call is no exception
IReadOnlyList<LockGuard> list = new Guards();
LockGuard b = list[0];                                // SCA1030
```

This applies to:

- a class that is declared in the project;
- a class of another assembly that has no member on the symbol list;
- a class or an interface that is a container only through what it derives from or implements, in an assembly that was not built with the analyzer (see [Code Not Built With the Analyzer](unchecked-code.md)).

This does not change the rule for:

| Type | Rule |
|---|---|
| `List<LockGuard>` itself | As new as what it was called on and with |
| A class of checked code with no container among its base classes, such as a marked `Bag<LockGuard>` | Its members' bodies are checked, so what they hand out is what they moved out |
| Such a class of unchecked code | Its members get a warning where they are used, unless they are on the symbol list |

## Task of a Completion Source

<xref:System.Threading.Tasks.TaskCompletionSource`1.Task?displayProperty=nameWithType> is a property, so the task that it gives is stored. Awaiting it is a read from a stored task (SCA1030), and returning it is [returning a container](returning-containers.md) that the source still holds (SCA1031). There is one exception, for the method that has just made the source: its one read of the task is a new value.

```cs
public Task<LockGuard> EnterAsync(CancellationToken token)
{
    var source = new TaskCompletionSource<LockGuard>();
    token.Register(() => source.TrySetCanceled());
    _waiters.Enqueue(source);
    return source.Task;                               // OK: made here, read once
}

// The same with no closure: the source is the state of a static lambda
token.Register(static state => ((TaskCompletionSource<LockGuard>)state!).TrySetCanceled(), source);

Task<LockGuard> EnterAsync() => _source.Task;         // SCA1031: one source in a field, and every caller gets the same task
```

The source need not be the method's own, and in a lock it is not: the queue of waiters holds it too, and so does the lambda that cancels it. What counts is where the task can come out.

Why is that one read enough? Any other code reaches the task through the `Task` property of a source that it did not make, and there the task is stored. A task gives its result only by value, so a read of the result there gets a warning anyway. The one read in the method that made the source can therefore be treated as new, and so can everything done with it:

```cs
return new ValueTask<LockGuard>(source.Task);         // OK
LockGuard held = await source.Task;                   // OK
var task = source.Task; /* ... */ return task;        // OK
```

#### Conditions

All of this has to hold:

| Condition | What does not count |
|---|---|
| The source is in an ordinary local of the method, of the type <xref:System.Threading.Tasks.TaskCompletionSource`1> itself | A local of a class that derives from it, a parameter, a field, a `ref` local |
| The method assigns the local once, in its declaration or after it, with `new TaskCompletionSource<T>(...)` and no argument that carries a non-copyable type | A local that is passed by `ref` or `out`, or that a pattern or a deconstruction declares |
| The method reads `source.Task` once, in whatever form, lambdas and local functions included | A second read |
| That read is in the body of the method itself | A read in a loop, a lambda, a local function or an iterator, or in a method that has a `goto` |
| A lambda, an anonymous method or a local function uses the source only as the method itself may (see below) | One that reads `source.Task`, assigns the local or passes it by `ref`: it can run any number of times |

What the method itself may do with the source, a lambda, an anonymous method or a local function may do too:

- call a member on it;
- pass, assign or return it by value;
- test it for null.

In the form with no closure the source becomes an `object` on the way. That is allowed because the [built-in framework symbol list](framework-symbol-list.md) lists <xref:System.Threading.Tasks.TaskCompletionSource`1> as [erasable](variance-and-erasable-types.md): it has no base class and no interface that could hand out a result. What a cast gets back out of the object is a stored source, so a task that is read from it is stored as well.

The rule does not look at the paths through the method, and it does not depend on [the move on the last use](msbuild-properties.md#turning-off-the-move-on-the-last-use).

#### Correct Code That Gets a Warning

The rule is strict. Three forms of correct code get a warning, and each has a clean rewrite:

```cs
// A tuple that holds the source is not one of the uses that the rule knows. A small class or record for a waiter is.
_waiters.Enqueue((source, token));                    // then 'return source.Task;' is SCA1031
_waiters.Enqueue(new Waiter(source, token));          // OK

// A local function or a lambda that makes the source and returns its task can run any number of times. A method is judged alone.
Task<LockGuard> Wait() { var source = new TaskCompletionSource<LockGuard>(); return source.Task; }   // SCA1031

// The task is read once. Where it is needed twice, read it into a local first.
var task = source.Task;
if (task.IsCanceled) { ... }
return task;                                          // OK
```

Where a source is not made in the method that hands out its task, as in a pool of sources, suppress the warning at the line that returns `source.Task`. Nothing in the code shows that the source is completed once and for one caller.

#### Value Task Sources

The same goes for a value task source. An <xref:System.Threading.Tasks.Sources.IValueTaskSource`1.GetResult*?displayProperty=nameWithType> that returns `_core.GetResult(token)` from a <xref:System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1> reads the result out of a stored struct, which keeps it until the source is reset (SCA1030).

The contract of the interface is that the result is taken once for each token, and nothing in the code shows that. Two value tasks made from one source and token are under [What Is Not Covered](not-covered.md). A library that builds its value tasks on such a source suppresses the warning at that one line of each source.

</div>
