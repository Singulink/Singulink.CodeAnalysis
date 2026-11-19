<div class="article">

# Moving Containers and Generic Moves

A value of a non-copyable type has one holder by construction, so it can be moved out of anything that can be passed by `ref`. A container is a reference, and copying a reference is allowed, so moving one requires more. This page gives the rules for moving a container, for a `[MovedFrom]` parameter of a container type, and for a generic move.

### At a Glance

| Rule | Warning |
|---|---|
| A container can be moved only out of a local that the method owns | SCA1033 |
| A `[MovedFrom]` parameter of a container type is the method's own, except where generic code passes in a slot | SCA1033 for a move out of one that is not |
| A method leaves in a container parameter only what its caller can own | SCA1041 |
| A generic move hands out and leaves behind only what it read from its `[MovedFrom]` parameters | SCA1041 |

## Moving a Container

Clearing one variable that holds a container says nothing about the others that hold the same reference. So a container can be moved only out of a local that the method owns. That is one that holds what the method created or got from a call, and that it has not stored anywhere or passed to something that keeps it. See [Locals That the Method Owns](owned-locals.md) for the test.

Moving a container out of a field, a parameter, an element, or a local that was assigned one of those, gets a warning (SCA1033). That holds for <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> and for any other method that takes a `[MovedFrom]` parameter. What such a move hands out is then as stored as what went in.

```cs
var pending = myLock.EnterAsync();
LockGuard a = await NonCopyable.Move(ref pending);   // OK: the method's own

LockGuard b = await NonCopyable.Move(ref _cached);   // SCA1033 and SCA1030: whoever else has the task can await it too
```

- **A `[MovedFrom]` parameter of the method itself** is its own, where its caller could see the container: see below.
- **The deconstruction of a tuple literal** assigns each element to its variable, so both locals of `var (first, second) = (a.EnterAsync(), b.EnterAsync());` are the method's own.

## Container Parameters

#### Where the Parameter Is the Method's Own

A `[MovedFrom]` parameter of a container type is the method's own only where its caller could see the container. That is where the method itself, the member that it overrides or implements, and the delegate type that it is converted to all give the parameter the container type.

Where one of them gives it the type of a type parameter, the caller is generic code. It hands over a slot of its own without knowing that the slot holds a task that was added twice. The parameter is then a stored container: the method uses it in place, and moving it on gets a warning (SCA1033).

```cs
delegate void MoveAction<[NonCopyable] T>([MovedFrom] ref T value);

// In Drainable<[NonCopyable] T>:
public void Drain(MoveAction<T> action) { for (int i = 0; i < _count; i++) action(ref _items[i]); }

tasks.Drain(([MovedFrom] ref Task<LockGuard> task) => { Log(task.IsCompleted); task = null!; });   // OK: used in place
tasks.Drain(([MovedFrom] ref Task<LockGuard> task) =>
{
    var mine = NonCopyable.Move(ref task);                                      // SCA1033: the slot is not the lambda's own
});
```

For a method that is used as a method group, the analyzer knows this only once it has seen the whole compilation, so that warning appears in a build and not while you type.

#### What a Move Leaves Behind

A caller's local that holds a container stays the caller's own after it was passed to a `[MovedFrom]` parameter. So the method may leave in the parameter only what the caller can own. It must assign a parameter of a container type, such as `[MovedFrom] ref Task<LockGuard> task`, one of these:

- `null`;
- a new value;
- a local that is the method's own, which is then the caller's and not the method's to return.

Leaving a field in it, such as a cached completed task, gets a warning at the assignment (SCA1041), and so does passing the parameter by reference to something that can assign anything to it.

```cs
static Task<LockGuard>? Take([MovedFrom] ref Task<LockGuard>? task)
{
    var taken = task;
    task = s_completed;                     // SCA1041: the caller would own a task that a field still holds
    return taken;
}
```

#### A Move Must Hand Back by Value

A method that takes a `[MovedFrom]` argument and returns a reference or a ref struct can hand back a reference to the caller's own variable, and the caller can write anything through it:

```cs
static ref Task<LockGuard>? Reset([MovedFrom] ref Task<LockGuard>? task) { task = null; return ref task; }

Reset(ref pending) = _stored;     // 'pending' now holds the stored task, and is no longer the method's own
LockGuard g = await pending;      // SCA1030
```

Such a method is not a generic move, and a local that was passed to it is no longer the caller's own.

## Generic Moves

The analyzer checks that a [generic move](index.md#moved-from) hands out and leaves behind only what it read from its `[MovedFrom]` parameters. Its body treats `T` as a value, so with a task bound to `T`, a static slot of type `T` may hold a task that somebody put there without owning it.

What such a method returns, through a type that mentions the type parameter, and what it leaves in a `[MovedFrom]` parameter, must be one of these:

- `default`;
- a value that it read from its `[MovedFrom]` parameters: the read itself, a local that is assigned nothing else, or the result of another [pure move](index.md#moved-from) of one of those.

Anything else gets a warning (SCA1041):

```cs
static T Swap([MovedFrom] ref T value)               // in Stash<[NonCopyable] T>, with a static slot
{
    T old = NonCopyable.Move(ref s_slot);
    s_slot = NonCopyable.Move(ref value);
    return old;                                      // SCA1041: not what was read from 'value'; or mark the return [Borrowed]
}
```

The check does not follow the value of a variable that a pattern declares, so in a generic move such a variable does not count as what the method read from its parameter. It gets a warning like any other value that came from elsewhere:

```cs
static T TakeOrCached([MovedFrom] ref T value)
{
    T taken = NonCopyable.Move(ref value);
    if (Peek() is { } cached)
        return cached;                               // SCA1041: what a pattern declares is not followed
    return taken;
}
```

#### Returning Something Else on Purpose

A method that returns something else on purpose, such as a swap with a slot that it keeps, marks its return value `[return: Borrowed]`. Its callers then treat a container that it hands out as stored, and a local that they passed to it as no longer their own. There is no mark for leaving something else behind.

The pure move is also what lets a caller read from a container that it moved: see [What the Generic Hands Out Is Stored](bound-containers.md#what-the-generic-hands-out-is-stored).

</div>
