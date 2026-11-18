<div class="article">

# Returning a Container

Which containers a method may return as its own, and the mark for one that it keeps.

## What Is Reported

The result of a call is a new value, so a method hands over the container that it returns. A method, a local function, a lambda or an operator
that returns, or yields, a container which something else still holds is reported at the `return` (`SCA1031`):

- a container that it read from a field, a property, an indexer, an element or a parameter;
- a local that was assigned one of those, or that a lambda captures;
- a local that the method did anything with that is not on the list below.

Returning a container that the method created or got from a call is fine, and so is a local that it only filled, also when it passed the local
to a method that only uses it. Where a local is not the method's own, the message says what made it so. One stored branch of a `?:`, a `??`
or a `switch` expression is enough to be reported. An
iterator goes on after it has yielded, so a local that it yields at two places, or in a loop that the local is declared outside of, is reported
as well. An array and an <xref:System.ArraySegment`1> are containers like any other: a method that returns an array field is reported. A <xref:System.Span`1>, a
<xref:System.ReadOnlySpan`1>, a <xref:System.Memory`1> and a <xref:System.ReadOnlyMemory`1> are not checked: they cannot become an interface, and every by-value read of an
element through one is reported whatever it came from.

## A Local That the Method Owns

**A local is the method's own by a list of what is allowed,** not of what is forbidden, whatever it holds. A list of what ends it always
misses a way: a marked collection that keeps the list (`bag.Add(list)`), a helper that hands its argument back, a `params` parameter, a view
of the local that is stored. A local is the method's own only if every value that is assigned to it is new and nothing else is done with it
but this:

- a member is called on it, also with `?.`, whose arguments bring in nothing stored and which hands out no reference, no view and no
  container of containers. An argument brings in nothing stored if its type carries no non-copyable type, if it is a new value of exactly
  the element type, or if it goes to a `[MovedFrom]` parameter. A delegate, an object that implements an interface over the element
  type, an array or a span of it does not qualify, new or not: code can push any stored container in;
- an element or an indexer of it is assigned a new value, or read by value;
- it is enumerated by a `foreach` with a by-value variable, tested for null or with a pattern that reads members which are not containers,
  returned, or moved with <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>.

Anything else ends it. For a local that holds containers of containers, such as a `Bag<Task<LockGuard>>`, a `List<List<LockGuard>>` or a
`Task<LockGuard>[]`, that is the whole list, being passed to another method included: such a local can take in a stored container by more
routes than can be named. A local that holds guards directly, such as a `List<LockGuard>` or a `LockGuard[]`, may also be used in these
ways, since no stored guard gets into it without a reported copy:

- It is passed by value to a parameter that is declared with the local's own type and is not `[Kept]`, of a method, a local function or a
  delegate whose result is no reference, no view and no ref struct and is not `[return: Borrowed]`. That is what keeps
  `Fill(list); return list;` clean. Not on the list: a parameter whose type is a type parameter, marked or not (`bag.Add(list)`); one
  whose declared type mentions a type parameter that is not marked, since the code behind it is not checked (`ArrayPool<T>.Return(T[])`
  keeps the array); a parameter of another type, such as an interface; an implicit `params` collection; a user-defined conversion or
  operator; an interpolated-string handler; and a call that hands out a view of the local, also through an `out` parameter.
- An element of an array is used in place (`guards[0].Dispose()`, `Release(ref guards[0])`), and a field of the object is read, used in
  place or assigned a new value.
- A view of it is made and used in one go: `foreach (ref LockGuard guard in CollectionsMarshal.AsSpan(list))` and
  `CollectionsMarshal.AsSpan(list)[0].Dispose()`. A view that is put into a variable, passed on or returned ends it.
- What a listed member makes of it that is the container under another name, a view that is no ref struct or the container as another
  type (`task.WaitAsync(timeout)`, `completion.Task`, `guards.AsMemory()`), is used where it is made:
  `await task.WaitAsync(timeout)`, `task.WaitAsync(timeout).IsCompleted`. Put into a variable, stored, passed on or returned, it ends
  the local being the method's own, whatever the local holds: whatever holds the one holds the other. The last use of the local is the
  exception, since nothing is left of the local to end: what is made takes its place and is as new as the local was, as in
  `return task.WaitAsync(timeout);` and `var waiting = task.WaitAsync(timeout); return await waiting;`.
- A task is awaited.

A member that the compiler calls for one of these uses, and that takes the local as a parameter, is other code that the local is
passed to: an extension `GetAwaiter`, an extension property in a pattern, or the constructor of an interpolated-string handler that
a call passes its argument on to. Where it keeps what it is given, the local is no longer the method's own.

These further uses keep the local the method's own to return and to move with <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>. They do not keep it a new container
for a by-value read at its [last use](last-use.md), which allows less: after `Fill(list);`, `return list;` is fine and
`LockGuard first = list[0];` is `SCA1030`.

```cs
List<LockGuard> Make(Bag<List<LockGuard>> bag)
{
    var list = new List<LockGuard>();
    Fill(list);                                   // OK: Fill(List<LockGuard>) only uses it
    bag.Add(list);                                // the bag keeps the list
    return list;                                  // SCA1031: 'list' is not the method's own anywhere in the method, because on line 5 it is passed to 'Bag<...>.Add(...)'
}
```

```cs
Bag<Task<LockGuard>> MakeBag()
{
    var bag = new Bag<Task<LockGuard>>();
    bag.Add(myLock.EnterTaskAsync());             // a new value: the bag stays the method's own
    bag.AddRange(new[] { _pending, _pending });   // a stored task goes in
    return bag;                                   // SCA1031
}
```

```cs
Task<LockGuard> GetCachedAsync() => _cached;      // SCA1031: every caller would get the same task

List<LockGuard> Build()
{
    var list = new List<LockGuard>();
    list.Add(myLock.Enter());
    return list;                                  // OK: the method's own
}
```

## The Borrowed Mark

A method that keeps what it returns says so with `[return: Borrowed]`. Its body may then return a container that it does not own, and its
callers treat the result as stored, as they treat what a property returns:

```cs
[return: Borrowed]
List<LockGuard> GetGuardsFor(string key) => _byKey[key];   // OK

foreach (LockGuard g in holder.GetGuardsFor("a")) { }      // SCA1030: a read from a stored container
holder.GetGuardsFor("a").Add(myLock.Enter());              // OK: used in place
```

- **Not checked, because every caller treats the result as stored anyway:** the getter of a property or an indexer, and a container that goes
  out through an `out` or `ref` parameter, which always lands in a variable.
- **`[Borrowed]` is kept like the other marks.** An override or an implementation must not have it where the member that it replaces does
  not, since a caller through that member takes the result for a new value (`SCA1040`). The same goes for a method or a lambda with the mark
  that is converted to a delegate whose return value is not marked. The other way round is allowed.

</div>
