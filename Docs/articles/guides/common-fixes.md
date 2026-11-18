<div class="article">

# Common Fixes

Most findings are fixed in one of a few ways.

## Fixes for Each Kind of Finding

**An element of a list:** read it through a span, which gives a reference to the element.

```cs
LockGuard first = guards[0];                              // SCA1030: a copy out of the list
guards[0].Dispose();                                      // SCA1030: disposes a copy

CollectionsMarshal.AsSpan(guards)[0].Dispose();           // OK: in place
ref LockGuard held = ref CollectionsMarshal.AsSpan(guards)[0];
```

**A loop over the elements:** declare a `ref` variable over a span of them.

```cs
foreach (var guard in guards) guard.Dispose();            // SCA1030: each 'guard' is a copy

foreach (ref var guard in CollectionsMarshal.AsSpan(guards)) guard.Dispose();   // OK
foreach (ref var guard in array.AsSpan()) guard.Dispose();                      // OK
```

**A value in a dictionary:** take it out with the member that removes it. To release every value, take a snapshot of the keys first.

```cs
LockGuard guard = held[key];                              // SCA1030: a copy, and the dictionary still has the guard

if (held.Remove(key, out var taken)) taken.Dispose();     // OK: taken out

int[] keys = [.. held.Keys];                              // OK: the keys only
foreach (int k in keys) { if (held.Remove(k, out var each)) each.Dispose(); }
```

**A field that is handed on:** move it out, which leaves the field empty.

```cs
return _guard;                                            // SCA1002
return NonCopyable.Move(ref _guard);                      // OK

Take(_guard);                                             // SCA1003
Take(NonCopyable.Move(ref _guard));                       // OK
```

**A variable that is used after it was handed on:** read what you need first. A variable is a new value where it is used for the last time,
so the hand-over has to be that last use.

```cs
_held = guard;                                            // SCA1001: 'guard' is read again below
Log(guard.Id);

int id = guard.Id;
_held = guard;                                            // OK: the last use
Log(id);
```

**A callback that works on a guard:** `Func<>` and `Action<>` take their arguments by value, and an `async` lambda cannot have a `ref`
parameter. Declare a delegate with a `ref` parameter. For `async` work over a guard, keep the guard in an object and pass that.

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

**A constructor that validates and then takes:** take the value through a `[MovedFrom] ref` parameter, so that the caller still has it when
the constructor throws. Read it and clear it in two statements with nothing between them.

```cs
try { return new UnitOfWork(scope, user); }               // SCA1003: the 'catch' can still run, so this is not the last use
catch { scope.Dispose(); throw; }

public UnitOfWork([MovedFrom] ref TransactionScope scope, string user)
{
    ArgumentException.ThrowIfNullOrEmpty(user);           // throws before anything is taken
    _scope = scope;
    scope = default;
}

try { return new UnitOfWork(ref scope, user); }           // OK
catch { scope.Dispose(); throw; }
```

**A task that a waiter completes later:** make the `TaskCompletionSource` in the method that returns its task, and read <xref:System.Threading.Tasks.Task> once, there.
See [What a listed member hands out](../concepts/listed-members.md).

```cs
var source = new TaskCompletionSource<LockGuard>();
_waiters.Enqueue(source);
return source.Task;                                       // OK: made here, read once
```

## Correct Code That Stays Reported

Three shapes are correct and are reported all the same. The analyzer follows a value through one method at a time, and takes a value that
is passed by value to be gone, so it cannot see that these are right.

**An `async` helper that fills a container which its caller awaits.** The helper goes on after its first `await`, when it has returned to
its caller, and nothing shows that the caller waits for it. Let the `async` method make the container and return it.

```cs
async Task FillAsync(List<LockGuard> guards)
{
    LockGuard guard = await myLock.EnterAsync();
    guards.Add(guard);                                    // SCA1032: uses 'guards' after an 'await'
}

async Task<List<LockGuard>> MakeAsync()                   // OK: makes the list and hands it over
{
    var guards = new List<LockGuard>();
    guards.Add(await myLock.EnterAsync());
    return guards;
}
```

**Handing a value over and keeping it on failure.** `TryWrite` and `TrySetResult` take the value only where they succeed, and a
`WriteAsync` that throws has not taken it. What the analyzer sees is a value that is passed by value and used again. There is no clean
form: suppress the finding at that place, with a comment that says why it is right.

```cs
#pragma warning disable SCA1003 // TryWrite takes the lease only where it returns true
if (!writer.TryWrite(lease))
    lease.Dispose();
#pragma warning restore SCA1003

try
{
#pragma warning disable SCA1003 // WriteAsync has not taken the lease where it throws
    await writer.WriteAsync(lease);
#pragma warning restore SCA1003
}
catch
{
    lease.Dispose();
    throw;
}
```

**A race between two tasks that hold a guard.** After <xref:System.Threading.Tasks.Task.WhenAny*?displayProperty=nameWithType> the winner and the loser are each awaited once, but the task that
`WhenAny` hands back is one of the two that the method still holds. The same goes here: suppress at that place, with the reason.

```cs
Task<LockGuard> winner = await Task.WhenAny(first, second);
Task<LockGuard> loser = ReferenceEquals(winner, first) ? second : first;

#pragma warning disable SCA1030 // each of the two tasks is awaited once
LockGuard lost = await loser;
lost.Dispose();
return await winner;
#pragma warning restore SCA1030
```

</div>
