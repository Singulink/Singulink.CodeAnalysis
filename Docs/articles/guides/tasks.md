<div class="article">

# Tasks and Async Code

A `Task<LockGuard>` holds its guard until somebody awaits it, and every await of the same task hands out the same guard again. So the analyzer treats a task like a list of one guard. This page shows how to await, store and make such tasks.

### At a Glance

| To | Write |
|---|---|
| Await a task that a call returns | `await myLock.EnterAsync()` |
| Start now and await later | A local that is awaited once and not used again |
| Await a value task in a field | `await NonCopyable.Move(ref _pending)` |
| Wait for several tasks | Await each one in turn |
| Limit how long a wait takes | Give the time limit to the lock, not to its task |
| Hand out a task that a waiter completes | Make the `TaskCompletionSource` in the method that returns its task |
| Pass a guard to a callback | A delegate with a `ref` parameter |

## Awaiting a Task

A task that a call has just returned is a new value, and awaiting it hands out a new guard. A task in a variable is stored up to the last use of the variable, so "start now, await later" needs nothing more:

```cs
var pending = myLock.EnterAsync();                       // start now
using var held = await pending;                          // await later: OK, 'pending' is not used again
```

A task that is used again after the `await`, or one in a field, still holds the guard, and awaiting it is a copy:

```cs
LockGuard a = await _cached!;                           // SCA1030: the field still holds the task
LockGuard b = await NonCopyable.Move(ref _cached)!;      // SCA1033 and SCA1030: the task cannot be moved out of a field
```

A `Task<LockGuard>` is a reference that others may hold, so moving it out of a field proves nothing. A `ValueTask<LockGuard>` is a struct that holds the guard, so it can be moved out of a field like the guard itself:

```cs
using var held = await NonCopyable.Move(ref _pending);   // OK: a value task is moved out of the field
```

## Waiting for Several Tasks

`Task.WhenAny` hands back one of the tasks that it was given, and the method still holds that task. The generic `Task.WhenAll` copies the results into an array. Await each task in turn instead: both tasks already run, so this waits no longer than `WhenAll` does:

```cs
var first = a.EnterAsync();
var second = b.EnterAsync();
using var g1 = await first;                              // OK: both tasks run already, so awaiting them one after
using var g2 = await second;                             //     the other waits no longer than WhenAll does
```

Where a race is what you need, see [A Race Between Two Tasks](correct-code-with-warnings.md#race-between-two-tasks-that-hold-a-guard).

## Time Limit on a Wait

Give the time limit to the lock, through the method or the cancellation token that it offers for that. The lock then ends the wait itself:

```cs
using var held = await myLock.TryEnterAsync(timeout);    // OK: the lock ends the wait
if (!held.IsEntered)
    return;
```

> [!WARNING]
> **A time limit on the task does not end the wait.** `await pending.WaitAsync(timeout)` and a `WhenAny` with a delay give up on the `await`, and the lock can still hand the guard to the task afterwards. Nobody awaits that task any more, so nobody releases the guard and the lock stays held. The analyzer reports copies, and there is none here, so `WaitAsync` on such a task gets no warning.

## Making a Task for a Waiter

A lock that queues its waiters hands each one a task from a `TaskCompletionSource<LockGuard>`. A read of the `Task` property of a source is a stored value, since the source keeps the task. The one exception is the method that has just made the source: there, the one read of `Task` is a new value:

```cs
public Task<LockGuard> EnterAsync(CancellationToken token)
{
    var source = new TaskCompletionSource<LockGuard>();
    token.Register(() => source.TrySetCanceled());
    _waiters.Enqueue(source);
    return source.Task;                               // OK: made here, read once
}
```

> [!NOTE]
> The rule is strict: the source must be in an ordinary local, made with `new`, and its `Task` read once, in the method body itself and not in a loop or a lambda. A source kept in a tuple is not followed: put it in a small class or record instead. The full conditions: [Task of a Completion Source](../rules/listed-members.md#task-of-a-completion-source).

## Callbacks

`Func<>` and `Action<>` take their arguments by value, and an `async` lambda cannot have a `ref` parameter. Declare a delegate with a `ref` parameter:

```cs
public delegate TResult GuardFunc<TResult>(ref LockGuard guard);

public TResult With<TResult>(GuardFunc<TResult> body)
{
    var guard = Enter();
    try { return body(ref guard); }
    finally { guard.Dispose(); }
}

int count = locks.With((ref LockGuard guard) => guard.Count);
```

For `async` work over a guard, keep the guard in an object and pass that.

## Async Methods

#### Async Methods That Take a List

An `async` method has returned to its caller at its first `await`. So a list parameter that it still uses after an `await` is kept (SCA1032), unless the parameter is marked `[Kept]`:

```cs
static async Task ReleaseLaterAsync(List<LockGuard> guards, SemaphoreSlim gate)
{
    await gate.WaitAsync();
    guards.Clear();                                                // SCA1032: the method has returned to its caller by now
}
```

Let the `async` method make the list and return it instead: see [Correct Code That Still Gets a Warning](correct-code-with-warnings.md).

#### Async Members of a Guard

An instance member of a non-copyable struct cannot be `async` or an iterator: the compiler copies `this` into the state machine, so the member runs on a copy (SCA1014). Instead, change the struct in a member that is not `async`, and return the task of a static method:

```cs
// In a [NonCopyable] struct:
public async ValueTask DisposeAsync() { await ReleaseAsync(_lock); _lock = null; }                // SCA1014: clears a copy

public ValueTask DisposeAsync() { var owner = _lock; _lock = null; return ReleaseAsync(owner); }  // OK
```

#### Awaiters

A custom awaiter that is a non-copyable struct gets a warning (SCA1015): the compiler copies an awaiter when the `await` suspends. Make it a class, or a copyable struct that refers to the state.

</div>
