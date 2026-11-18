<div class="article">

# What a Listed Member Hands Out

A member on the list hands out a value that is **as new as what it was called on and with**. Called on a new value it gives a new value. Called on a
stored container it gives a copy, which is reported (`SCA1030`).

## Examples

```cs
LockGuard a = await myLock.EnterAsync();                       // OK: read from a new value
LockGuard b = await myLock.EnterAsync().ConfigureAwait(false); // OK

Task<LockGuard> stored = EnterTaskAsync();
LockGuard c = await stored;                                    // SCA1030: the task still holds the guard
bool done = stored.IsCompleted;                                // (the task is used again)
LockGuard d = list[0];                                         // SCA1030: the list still holds the guard

foreach (LockGuard item in CreateGuards()) { }                 // OK: the list is a new value
foreach (LockGuard item in list) { }                           // SCA1030

ref LockGuard r = ref span[1];                                 // OK: a reference
span[1].Dispose();                                             // OK: called in place
foreach (ref LockGuard item in span) { }                       // OK
```

## Views, Members That Take a Value Out, and Waiting for Tasks

A view, such as an <xref:System.ArraySegment`1>, never owns its elements. An element that its indexer or its enumerator hands out by value is a copy,
whatever the view came from:

```cs
LockGuard e = new ArraySegment<LockGuard>(CreateArray())[0];   // SCA1030: the array holds the guard
```

Members that take the value out for good, such as <xref:System.Collections.Generic.Stack`1.Pop*?displayProperty=nameWithType> and <xref:System.Collections.Generic.Queue`1.Dequeue*?displayProperty=nameWithType>, are listed as handing out a new value whatever they
are called on. That is about a value of a non-copyable type, which leaves no second instance behind. A container that such a member hands out,
as from a `Stack<Task<LockGuard>>`, is a reference that whoever put it in may still hold, so it is as new as what the member was called on and
with, like the result of any other listed member.

<xref:System.Threading.Tasks.Task.WhenAny*?displayProperty=nameWithType> over tasks of a guard hands out one of the tasks that it was given, so awaiting that task is a read from a stored one. The
generic <xref:System.Threading.Tasks.Task.WhenAll*?displayProperty=nameWithType> copies the results into an array and is not listed. To wait for several tasks without that, cast one of them:
`await Task.WhenAll((Task)first, second);` picks the overload that returns no results. The tasks are passed on by that call, though, and a
task that was passed to anything is no longer the method's own: a result that is read from it afterwards is reported (`SCA1030`), as it is
after a <xref:System.Threading.Tasks.Task.WhenAny*?displayProperty=nameWithType> with a delay. Two forms stay clean:

```cs
var first = a.EnterTaskAsync();
var second = b.EnterTaskAsync();
using var g1 = await first;                              // OK: both tasks run already, so awaiting them one after
using var g2 = await second;                             //     the other waits no longer than WhenAll does

var pending = myLock.EnterTaskAsync();
using var held = await pending.WaitAsync(timeout);       // OK: a time limit without WhenAny and a delay
```

## Classes That Derive From a Listed Container

**A class that derives from a listed container is never a new container for the members that it inherits.** With
`class Guards : List<LockGuard>`, the indexer of <xref:System.Collections.Generic.List`1> hands out what the object holds "as new as the object", and nothing checks the
code of `Guards` against that: it can keep `this`, hand it out, or fill itself from anywhere. So a by-value read through an inherited
member is a read from a stored container (`SCA1030`) whatever the object was created from, also at the
[last use](last-use.md) of a local that the method owns, and the object is stored where it is converted to the container
that it derives from or to an interface of it:

```cs
sealed class Guards : List<LockGuard>
{
    private static readonly List<Guards> s_all = new();
    public void Register() => s_all.Add(this);        // nothing in the code of a caller shows this
}

var guards = new Guards();
guards.Add(myLock.Enter());                           // OK: used in place
CollectionsMarshal.AsSpan(guards)[0].Dispose();       // OK: used in place
LockGuard a = guards[0];                              // SCA1030, also where 'guards' is not used again
foreach (LockGuard g in MakeGuards()) { }             // SCA1030: a Guards from a call is no exception
IReadOnlyList<LockGuard> list = new Guards();
LockGuard b = list[0];                                // SCA1030
```

That goes for a class that is declared in the project, and for one of another assembly that has no member on the list. A `List<LockGuard>`
itself keeps its rule, and so does a type that has no container among its base classes: what its own members hand out is checked in
their bodies if it is checked code, as with a marked generic such as `Bag<LockGuard>`, which hands out only what it moved out, or is
reported where an unlisted member is used.

## Tasks in a Variable

A task or a value task in a variable is a stored value up to the [last use](last-use.md) of the variable, so a read of its
result that the method follows with another use is reported. "Start now, await later" needs nothing more:

```cs
var pending = myLock.EnterAsync();                       // start now
using var held = await pending;                          // await later: OK, 'pending' is not used again
```

A value task in a field, or in a variable that is used again afterwards, is read with a move:
`using var held = await NonCopyable.Move(ref _pending);`.

## The Task of a Completion Source

<xref:System.Threading.Tasks.TaskCompletionSource`1.Task?displayProperty=nameWithType> is a property, so the task that it gives is stored: awaiting it is a read from a stored task (`SCA1030`),
and returning it is [returning a container](returning-containers.md) that the source still holds (`SCA1031`). There is one exception, for
the method that has just made the source. Its one read of the task is a new value:

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

The source need not be the method's own, and in a lock it is not: the queue of waiters holds it too, and so does the lambda that cancels
it. What counts is where the task can come out. Everything else reaches it through the same property of a source that it did not make,
where it is stored, and a task gives its result by value only, which is reported there. So the read in the method that made the source is
the one place where the task is new, and `return new ValueTask<LockGuard>(source.Task);`, `await source.Task` and
`var task = source.Task; ... return task;` are clean as well. All of this has to hold:

- The source is in an ordinary local of the method, of the type <xref:System.Threading.Tasks.TaskCompletionSource`1> itself and not of a class that derives from
  it. A parameter, a field or a `ref` local does not count.
- The method assigns the local once, in its declaration or after it, and what it assigns is `new TaskCompletionSource<T>(...)` with no
  argument that carries a non-copyable type. A local that is passed by `ref` or `out`, or that a pattern or a deconstruction declares, does
  not count.
- The method reads `source.Task` once, in whatever form, lambdas and local functions included.
- That read is in the body of the method itself: not in a loop, a lambda, a local function or an iterator, and the method has no `goto`.
- A lambda, an anonymous method or a local function may use the source as the method itself may: call a member on it, pass, assign or
  return it by value, test it for null. It may not read `source.Task`, assign the local or pass it by `ref`: it can run any number of
  times.

In the form with no closure the source becomes an `object` on the way. That is allowed because the built-in list has
<xref:System.Threading.Tasks.TaskCompletionSource`1> as [erasable](variance-and-erasable-types.md): it has no base class and no interface that could hand out a result.
What a cast gets back out of the object is a stored source, so a task that is read from it is stored as well.

The rule does not look at the paths through the method, and it does not depend on
[the move on the last use](../guides/turning-rules-down.md#turning-off-the-move-on-the-last-use). It is strict, and three forms of correct code that it reports have a
rewrite that is clean:

```cs
// A tuple that holds the source is not one of the uses that the rule knows. A small class or record for a waiter is.
_waiters.Enqueue((source, token));                    // then 'return source.Task;' is SCA1031
_waiters.Enqueue(new Waiter(source, token));          // OK

// A local function or a lambda that makes the source and returns its task can run any number of times. A method is judged alone.
Task<LockGuard> Wait() { var source = new TaskCompletionSource<LockGuard>(); ...; return source.Task; }

// The task is read once. Where it is needed twice, read it into a local first.
var task = source.Task;
if (task.IsCanceled) { ... }
return task;                                          // OK
```

Where a source is not made in the method that hands out its task, as in a pool of sources, the finding at the line that returns
`source.Task` is suppressed: nothing in the code shows that the source is completed once and for one caller.

The same goes for a value task source. An <xref:System.Threading.Tasks.Sources.IValueTaskSource`1.GetResult*?displayProperty=nameWithType> that returns `_core.GetResult(token)` from a
<xref:System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1> reads the result out of a stored struct, which keeps it until the source is reset (`SCA1030`). The
contract of the interface is that the result is taken once for each token, and nothing in the code shows that (two value tasks made from
one source and token are under [Not covered](not-covered.md)). A library that builds its
value tasks on such a source suppresses the finding at that one line of each source.

</div>
