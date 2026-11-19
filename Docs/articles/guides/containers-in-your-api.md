<div class="article">

# Containers in Your API

A method that returns a list or a task of guards hands it over, and a method that takes one only uses it during the call: that is what callers assume. This page shows how an API says otherwise, with `[return: Borrowed]` and `[Kept]`, and what the analyzer checks.

### At a Glance

| Your method | What to write |
|---|---|
| Returns a container that it made | Nothing: it is the caller's now |
| Returns a container that it still holds, such as one from a field | `[return: Borrowed]` |
| Takes a container and only uses it during the call | Nothing |
| Takes a container and keeps it, in a field or after an `await` | `[Kept]` on the parameter |
| Exposes a container as a property | Nothing: a property is always treated as stored |

## Returning a Container

The result of a call is a new value, so a method that returns a container hands it over, and the caller may read from it as its own. So the analyzer checks that the method really does not hold it any more:

```cs
Task<LockGuard> GetCachedAsync() => _cached;      // SCA1031: every caller would get the same task

List<LockGuard> Build()
{
    var list = new List<LockGuard>();
    list.Add(myLock.Enter());
    return list;                                  // OK: the method's own
}
```

A container that the method created, or got from a call of checked code, is fine to return. Filling it is fine. So is passing it to a method that takes exactly the container's own type, does not mark the parameter `[Kept]`, and only uses it. The message says why a local is not the method's own. The exact test: [Locals That the Method Owns](../rules/owned-locals.md).

> [!NOTE]
> A container from a member marked `[return: Borrowed]`, or from code that was not built with the analyzer, is stored, and returning it gets a warning. See [Containers Handed Out by Unchecked Code](../rules/unchecked-code.md#containers-handed-out-by-unchecked-code).

#### Containers That You Keep

A method that keeps what it returns says so with `[return: Borrowed]`. Its callers then treat the result as stored, as they treat what a property returns:

```cs
[return: Borrowed]
List<LockGuard> GetGuardsFor(string key) => _byKey[key];   // OK

foreach (LockGuard g in holder.GetGuardsFor("a")) { }      // SCA1030: a read from a stored container
holder.GetGuardsFor("a").Add(myLock.Enter());              // OK: used in place
```

A property or an indexer needs no mark: every caller treats what it returns as stored anyway.

## Taking a Container

The analyzer assumes that a method uses a container that it is given only during the call, so the caller still owns the container afterwards. A method that keeps it says so with `[Kept]`:

```cs
void Fill(List<LockGuard> list) => list.Add(myLock.Enter());       // OK: only uses it

void Register(List<LockGuard> list) => _all = list;                // SCA1032: keeps it
void Register([Kept] List<LockGuard> list) => _all = list;         // OK
```

Some members are assumed to keep what they are given, even with no mark:

| Member | Example |
|---|---|
| A constructor | `new Holder(list)` |
| A property or indexer setter | `holder.Guards = list` |
| A member of a generic that is not marked, for what goes to its type parameter | `outer.Add(inner)` |

#### Async Methods Keep What They Use Later

An `async` method has returned to its caller at its first `await`, and an iterator runs its whole body after the call. So such a method keeps every container parameter that it uses after that point:

```cs
static async Task ReleaseLaterAsync(List<LockGuard> guards, SemaphoreSlim gate)
{
    await gate.WaitAsync();
    guards.Clear();                                                // SCA1032: the method has returned to its caller by now
}

static async Task ReleaseLaterAsync([Kept] List<LockGuard> guards, SemaphoreSlim gate) { /* ... */ }   // OK, and the caller is told
```

The mark is in the signature, so it holds through an interface, an override and a referenced assembly. An interface method whose `async` implementation needs the mark needs it too.

#### Wrapping a Task

A method that wraps a task that it was given has no form without a warning: it keeps the task, and it reads from a stored task.

```cs
ValueTask<LockGuard> Wrap(Task<LockGuard> task) => new(task);   // SCA1032 and SCA1030
```

Return the `Task<LockGuard>` as it is, take the task through a `[MovedFrom] ref` parameter, or suppress the warning in the wrapper.

## Overrides and Delegates

The marks are promises to the callers, so an override, an implementation or a delegate must not add `[return: Borrowed]` or `[Kept]` where the member that it stands for has none (SCA1040). See [Keeping Marks on Overrides and Delegates](../rules/keeping-marks.md).

## Converting Containers

Converting a container to a type that hides the guard, such as `object` or a non-generic `IList`, gets a warning (SCA1022, SCA1023): the code that receives it cannot be checked. A `Task<LockGuard>` may become a plain `Task`, since a `Task` hands out no result. See [Containers and Conversions](../rules/conversions.md).

## Next Steps

Here is where to go from here:

- [Writing a Move Method](writing-a-move.md) - Taking a value from the caller with `[MovedFrom]`.
- [Returning a Container](../rules/returning-containers.md) and [Keeping a Container That Is Passed In](../rules/keeping-containers.md) - The exact rules.

</div>
