<div class="article">

# Locals That the Method Owns

A local that holds a container, such as a list or a task, is the method's own if nothing else can hold the same container. Only such a local may be [returned](returning-containers.md), [moved from](moves-of-containers.md#moving-a-container), and read as a new container at [its last use](last-use.md#locals-that-hold-a-container). This page gives the test.

### At a Glance

| A local that | Stays the method's own? |
|---|---|
| Was assigned only new values: what the method created or got from a call of checked code | Yes, to start with |
| Has members called on it with new arguments, such as `list.Add(myLock.Enter())` | Yes |
| Is passed to a parameter of exactly its own type, not `[Kept]` (a local of guards only) | Yes |
| Is used through a view in one go, such as `CollectionsMarshal.AsSpan(list)[0].Dispose()` | Yes |
| Is given a stored container or value, or passed to anything else | No |
| Has a view or another name of it kept in a variable | No, except at its last use |

The test names what is allowed, not what is forbidden. A catalogue of what ends ownership always misses a way: a marked collection that keeps the list (`bag.Add(list)`), a helper that hands its argument back, a `params` parameter, a stored view.

## What Any Local May Do

Every value that is assigned to the local must be new. Then only these uses keep it the method's own:

| Use | Condition |
|---|---|
| A member is called on it, even with `?.` | Its arguments bring in nothing stored, and it hands out no reference, no view and no container of containers |
| An element or an indexer of it is assigned a new value, or read by value | |
| It is enumerated by a `foreach` with a by-value variable | |
| It is tested for null, or with a pattern that reads members which are not containers | |
| It is returned, or moved with <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> | |

#### Arguments That Bring In Nothing Stored

An argument brings in nothing stored if its type carries no non-copyable type, if it is a new value of exactly the element type, or if it goes to a `[MovedFrom]` parameter:

```cs
var list = new List<LockGuard>();
list.Add(myLock.Enter());                     // OK: a new guard of exactly the element type
list.Add(NonCopyable.Move(ref _guard));       // OK: a moved value is new
return list;                                  // OK: the method's own
```

None of these qualifies, new or not: a delegate, an object that implements an interface over the element type, or an array or a span of the element type. Through any of them, code can push a stored container in.

#### Locals That Hold Containers of Containers

For a local that holds containers of containers, such as a `Bag<Task<LockGuard>>`, a `List<List<LockGuard>>` or a `Task<LockGuard>[]`, the table above is the whole list. Passing the local to another method in any way ends its ownership. Such a local can take in a stored container by more routes than can be named:

```cs
var tasks = new List<Task<LockGuard>>();
tasks.Add(myLock.EnterAsync());               // OK: a new task
tasks.Add(_pending);                          // a stored task goes in
return tasks;                                 // SCA1031
```

## What a Local of Guards May Also Do

A local that holds guards directly, such as a `List<LockGuard>` or a `LockGuard[]`, may be used in a few more ways. A stored guard cannot get into it without a warning for the copy, so these uses cannot bring one in.

#### Passing It to a Method That Only Uses It

It may be passed by value to a parameter that is declared with the local's own type and is not `[Kept]`. The method, local function or delegate must return no reference, no view and no ref struct, and must not be `[return: Borrowed]`:

```cs
var list = new List<LockGuard>();
Fill(list);                                   // OK: Fill(List<LockGuard>) only uses it
return list;                                  // OK: still the method's own
```

Passing it to any other parameter ends its ownership:

```cs
var list = new List<LockGuard>();
Inspect(list);                                // a parameter of another type
return list;                                  // SCA1031
```

| Passed to | Why it ends the local |
|---|---|
| A parameter whose type is a type parameter, marked or not, as in `bag.Add(list)` | It is [assumed to keep](keeping-containers.md) what it is given |
| A parameter whose declared type mentions a type parameter that is not marked | The code behind it is not checked: `ArrayPool<T>.Return(T[])` keeps the array |
| A parameter of another type, such as an interface | It is not the local's own type |
| An implicit `params` collection, a user-defined conversion or operator, an interpolated-string handler | These are other code |
| A call that hands out a view of the local, even through an `out` parameter | The view can outlive the call |

#### Using Parts of It in Place

An element of an array may be used in place, and a field of the object may be read, used in place or assigned a new value:

```cs
var guards = new LockGuard[2];
guards[0] = myLock.Enter();                   // OK: a new value into an element
guards[0].Dispose();                          // OK: an element used in place
return guards;                                // OK
```

#### Views Used in One Go

A view of it may be made and used in one go. A view that is put into a variable, passed on or returned ends its ownership:

```cs
CollectionsMarshal.AsSpan(list)[0].Dispose(); // OK: a view used in one go
return list;                                  // OK

var span = CollectionsMarshal.AsSpan(list);   // a view in a variable
return list;                                  // SCA1031
```

#### Awaiting

A task may be awaited.

## Container Under Another Name

Some members on the [symbol list](symbol-list-format.md) hand back the same container in another form: a view that is no ref struct, or the container as another type, such as `task.WaitAsync(timeout)`, `completion.Task` or `guards.AsMemory()`. Using that result on the spot is fine:

```cs
var task = myLock.EnterAsync();
bool done = task.WaitAsync(timeout).IsCompleted;  // OK: used where it is made
LockGuard held = await task.WaitAsync(timeout);   // OK: used where it is made, at the last use
```

Keeping the result anywhere means that the local is no longer the method's own, whatever the local holds, because whatever holds the result holds the container too:

```cs
var task = myLock.EnterAsync();
var waiting = task.WaitAsync(timeout);        // 'task' is no longer the method's own...
LockGuard held = await task;                  // SCA1030: ...so this reads from a stored task
```

This does not apply at the last use of the local, since the local is not used afterwards. The result takes the local's place, and is as new as the local was:

```cs
var task = myLock.EnterAsync();
return task.WaitAsync(timeout);               // OK: the last use, so the result takes the local's place
```

```cs
var task = myLock.EnterAsync();
var waiting = task.WaitAsync(timeout);        // the last use of 'task'
LockGuard held = await waiting;               // OK: 'waiting' took the place of 'task'
```

## Members That the Compiler Calls

For some uses, the compiler calls a member that takes the local as a parameter, and that member is other code that the local is passed to. Such a member is an extension `GetAwaiter`, an extension property in a pattern, or the constructor of an interpolated-string handler that a call passes its argument on to. If the member keeps what it is given, the local is no longer the method's own:

```cs
// With: public static TaskAwaiter<LockGuard> GetAwaiter([Kept] this List<LockGuard> list)
var list = new List<LockGuard>();
LockGuard g = await list;                     // the extension GetAwaiter keeps 'list'
return list;                                  // SCA1031: the GetAwaiter that the await calls kept it
```

## Owning Is Not Being New

The extra uses of a local of guards keep it the method's own, so it may be returned or moved. They do not keep it a new container for a by-value read at its [last use](last-use.md), which allows fewer uses:

```cs
var list = new List<LockGuard>();
Fill(list);
return list[0];                               // SCA1030: the method's own, but no longer new
```

</div>
