<div class="article">

# Correct Code That Still Gets a Warning

Three shapes of correct code get a warning all the same. The analyzer follows a value through one method at a time, and it assumes that a value passed by value is gone, so it cannot see that these are right. This page gives the clean form or the suppression for each.

### At a Glance

| Shape | What to do |
|---|---|
| An `async` helper that fills a container which its caller awaits | Let the `async` method make the container and return it |
| Handing a value over and keeping it on failure | Suppress the warning where it stands, with the reason |
| A race between two tasks that hold a guard | Suppress the warning where it stands, with the reason |

> [!NOTE]
> Suppress one warning at the place where it stands, with a comment that says why the code is right. Do not turn the rule down for the project: that would hide every real copy that the rule warns about as well.

## Async Helper That Fills a Container

The helper keeps running after its first `await`, when it has already
returned to its caller, and nothing shows that the caller waits for it. Let the `async` method make the container and return it.

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

## Handing a Value Over and Keeping It on Failure

`TryWrite` and `TrySetResult` take the value only when they succeed, and a
`WriteAsync` that throws has not taken it. What the analyzer sees is a value that is passed by value and used again. There is no clean
form: suppress the warning at that place, with a comment that says why it is right.

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

## Race Between Two Tasks That Hold a Guard

After <xref:System.Threading.Tasks.Task.WhenAny*?displayProperty=nameWithType> the winner and the loser are each awaited once, but the task that
`WhenAny` hands back is one of the two that the method still holds, so awaiting it gets a warning. Here too, suppress the warning at that
place, with the reason.

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
