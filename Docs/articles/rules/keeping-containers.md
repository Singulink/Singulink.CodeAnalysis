<div class="article">

# Keeping a Container That Is Passed In

A method that is given a container only uses it during the call, so the caller still owns it afterwards. The caller can fill a list, pass it to helpers and return it as its own. A method that keeps the container says so with `[Kept]` on the parameter. This page gives the exact rule.

### At a Glance

These are assumed to keep what they are given:

- a parameter marked `[Kept]`;
- a constructor, and the setter of a property or an indexer;
- a member of an unmarked generic, for what is bound to its type parameter, as in `outer.Add(inner)`, and for a container whose type mentions that type parameter, as in `ArrayPool<LockGuard>.Shared.Return(buffer)`.

A method that keeps a container parameter without the mark gets a warning where it first does so (SCA1032).

## What Keeps a Container

| Passed to | Kept? |
|---|---|
| A parameter marked `[Kept]`, including the receiver of an extension block (`extension([Kept] List<LockGuard> list)`) | Yes |
| A parameter of a constructor, and the value of a property or indexer setter | Yes |
| A parameter whose type is a type parameter, marked `[NonCopyable]` or not | Yes |
| A parameter whose declared type mentions a type parameter that is not marked, as `ArrayPool<T>.Return(T[])` does | Yes, unless the [symbol list](symbol-list-format.md) marks the member `borrows`, as for `Array.Reverse<T>(T[])` |
| A parameter of a type other than that of the caller's local, an implicit `params` collection, a conversion, an operator, a handler | Yes, as far as the caller's local goes |
| A parameter of the local's own type, of a method, a local function or a delegate that hands back no reference and no view | No |

The last two rows are about the caller: its local stays its own only in the last case, and only if the local holds guards directly. See [Locals That the Method Owns](owned-locals.md#passing-it-to-a-method-that-only-uses-it) for the whole list.

## Keeping a Parameter Without the Mark

A method that keeps a container parameter without the mark gets a warning where it first does so (SCA1032). A method keeps a parameter when it:

- assigns it to a field, an element or a property;
- writes it through a reference;
- captures it in a lambda, or in a delegate for one of its methods;
- passes it on to something that keeps it.

```cs
void Fill(List<LockGuard> list) => list.Add(myLock.Enter());       // OK: only uses it

void Register(List<LockGuard> list) => _all = list;                // SCA1032: keeps it
void Register([Kept] List<LockGuard> list) => _all = list;         // OK

void Register(List<LockGuard> list)
{
    ref List<LockGuard> slot = ref _all;
    slot = list;                                                   // SCA1032: a reference can refer to anything that outlives the call
}

List<LockGuard> Build(Holder holder)
{
    var list = new List<LockGuard>();
    holder.Fill(list);
    holder.Register(list);
    return list;                                                   // SCA1031: the holder has it too
}
```

A parameter whose type is a type parameter keeps what it is given, whether the type parameter is marked `[NonCopyable]` or not, and whether the container is passed by value or by reference. So a collection of the project's own keeps the list, just as a <xref:System.Collections.Generic.List`1> would:

```cs
static void Register(List<LockGuard> list) => s_registered.Add(list);   // SCA1032: kept in a collection of the project's own
```

A [pure move](moves-of-containers.md#generic-moves) is the exception: what goes into `NonCopyable.Move(ref list)` comes out as its result.

## What a Parameter May Do

A parameter that is not `[Kept]` may be used only in the ways that the table below lists. The analyzer checks against the uses that are allowed, because a catalogue of the ways to keep a container always misses one.

The question is only whether anything still holds the container once the method has returned. So this table is shorter than the one for [a local that the method owns](owned-locals.md), and allows more:

```cs
void Use(List<LockGuard> list)
{
    list.Add(myLock.Enter());                         // OK: a member of its own
    CollectionsMarshal.AsSpan(list)[0].Dispose();     // OK: a view that stays in the method
    ArgumentNullException.ThrowIfNull(list);          // OK: a parameter that does not keep
}
```

One difference matters: a local allows only a use on the spot, while the check follows a parameter into every local that it is copied into, and from there into wherever that local goes:

```cs
void Register(List<LockGuard> list) { var other = list; _all = other; }   // SCA1032: 'other' is 'list'
```

| Use of the parameter | Keeps it? |
|---|---|
| Any member of its own that is used on it, even with `?.`, whatever the member takes in or hands out | No |
| A field or a property of it that is read or set, an element that is used in place | No |
| A reference or a view of it that stays in the method (`ref LockGuard first = ref span[0];`, `Span<LockGuard> view = CollectionsMarshal.AsSpan(list);`) | No |
| Passing it, by value or by reference and whatever the declared type of the parameter, to a parameter that does not keep (`ArgumentNullException.ThrowIfNull(list);`) | No |
| A test, a pattern, a `foreach`, an `await`, a spread and a `with`, as far as the members that those call are its own | No |
| Returning it, which is judged on its own: see [Returning a Container](returning-containers.md) | No |
| A write of the variable | No |
| A store into anything but a local of the method | Yes. A store into a local is the container under another name, and the check follows it |
| A capture, as in `() => list.Clear()` | Yes |
| Passing it in any form to what keeps | Yes: see [What the Keeping Check Follows](what-keeping-follows.md) |
| Anything else that takes it as a value, such as a member called on a `dynamic` reference to it, or `__makeref` | A warning too (SCA1032, with a text of its own): a form that is not listed gets a warning instead of leaving a gap |

What this does not see is a member of the framework that keeps an argument which it takes as `object`, such as the state of `CancellationToken.UnsafeRegister(callback, state)`: nothing in its signature says so. The [conversion that hides the type](conversions.md) and [the symbol list](symbol-list-format.md) deal with that case.

## Methods That Go On After They Return

**A method that keeps running after it has returned keeps what it still uses.** An iterator runs its whole body after the call that creates it has returned. An `async` method has already returned to its caller when it runs what follows its first `await`.

- An iterator keeps every container parameter that it uses.
- An `async` method keeps every container parameter that it uses on a path after an `await`, including an `await foreach` or an `await using` (SCA1032).
- A lambda and a local function are judged the same way.
- What an `async` method does before its first `await` runs during the call and keeps nothing.

```cs
static async Task ReleaseLaterAsync(List<LockGuard> guards, SemaphoreSlim gate)
{
    await gate.WaitAsync();
    guards.Clear();                                                // SCA1032: the method has returned to its caller by now
}

static async Task ReleaseLaterAsync([Kept] List<LockGuard> guards, SemaphoreSlim gate) { /* ... */ }   // OK, and the caller is told

static async Task CountAsync(List<LockGuard> guards)
{
    int count = guards.Count;                                      // OK: before the first 'await'
    await Task.Delay(count);
}
```

The mark is in the signature, so it holds through an interface, an override and a referenced assembly, where the caller cannot see how the method is written. An interface method whose `async` implementation needs the mark needs it too. Overrides and delegates must keep the mark: see [Kept Mark](keeping-marks.md#kept-mark).

## Wrapping a Task

**A method that wraps a task which it was given always gets a warning:**

```cs
ValueTask<LockGuard> Wrap(Task<LockGuard> task) => new(task);   // SCA1032 and SCA1030
```

It keeps the task (SCA1032) and reads from a stored one (SCA1030). No annotation changes the second, since a caller could pass the same field twice, and `[Kept]` only moves the question to the caller. Instead:

- return the `Task<LockGuard>` as it is;
- take the task through a `[MovedFrom] ref` parameter, which the caller has to own;
- or suppress the warning in the wrapper.

</div>
