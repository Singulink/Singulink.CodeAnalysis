<div class="article">

# Correct Code That Still Gets a Warning

Three shapes of correct code get a warning all the same. The analyzer follows a value through one method at a time, and it assumes that a value passed by value is gone, so it cannot see that these are right. This page gives the clean form or the suppression for each.

### At a Glance

| Shape | What to do |
|---|---|
| An `async` helper that fills a container which its caller awaits | Let the `async` method make the container and return it |
| Handing a value over in a `try` block and keeping it where the receiver throws before it takes the value | Suppress the warning where it stands, with the reason |
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

A member that takes the value only where it returns true needs nothing, where the symbol list says so. The framework symbol list says it of
`ChannelWriter<T>.TryWrite`, `Dictionary<TKey, TValue>.TryAdd` and others (see
[Members That Take a Value Only Where They Return True](../rules/framework-symbol-list.md#members-that-take-a-value-only-where-they-return-true)),
so the branch where they returned false is clean. A line in a symbol list file says the same of a member of a library or of your own (see
[Symbol List](symbol-list.md#a-member-that-takes-a-value-only-where-it-returns-true)).

```cs
if (!_writer.TryWrite(lease))
    lease.Dispose();                                      // OK: TryWrite has not taken the lease here
```

A member that throws may have taken the value, so the analyzer reports a `catch` that uses the variable after a hand-off (SCA1016). Most
code can do the work that can fail first, and hand the value over after the `try` statement: see
[Cleaning Up When Something Throws](cleanup-when-throwing.md#into-a-collection).

Where the hand-off itself has to be in the `try` block, as an `await` of `ChannelWriter<T>.WriteAsync` is, the code is right only where the
member throws before it takes the value. A channel can throw after it has taken the value where it has a callback for dropped items, or
where it allows synchronous continuations and a reader runs its continuation at once. For a channel that has neither, the code is right:
suppress the warning at that place, with a comment that says why.

```cs
try
{
    await _channel.Writer.WriteAsync(lease);
}
catch
{
#pragma warning disable SCA1016 // _channel can only throw before it takes the lease
    lease.Dispose();
#pragma warning restore SCA1016
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

## Under the Updated Memory Safety Rules

In a project that turns on the [updated memory safety rules](../rules/updated-memory-safety-rules.md) of C#, these reports are errors, and a
`#pragma` alone does not suppress them. Put the code that is reported in an unsafe context first, and suppress the warning there:

```cs
catch
{
#pragma warning disable SCA1016 // _channel can only throw before it takes the lease
    unsafe(lease).Dispose();
#pragma warning restore SCA1016
    throw;
}
```

`unsafe(lease).Dispose();` covers the statement and copies nothing. An `unsafe { }` block around the statement does the same.

For the race, `unsafe(await loser)` and `unsafe(await winner)` hold each `await`, with the same `#pragma` around them.

</div>
