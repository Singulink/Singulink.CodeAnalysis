<div class="article">

# Keeping a Container That Is Passed In

A method that is given a container only uses it during the call, so the caller still owns it afterwards: it can fill a list, pass it to
helpers and return it as its own. A method that keeps the container says so with `[Kept]` on the parameter. A constructor and the setter of a
property or an indexer are taken to keep what they are given, and so is a member of an unmarked generic for what is bound to its type
parameter, as in `outer.Add(inner)`, and for a container whose type mentions that type parameter, as in
`ArrayPool<LockGuard>.Shared.Return(buffer)`.

## What Keeps a Container

| What the container is passed to | Is the caller's local still its own? |
|---|---|
| A parameter marked `[Kept]`, also the receiver of an extension block (`extension([Kept] List<LockGuard> list)`) | No |
| A parameter of a constructor, and the value of a property or indexer setter | No |
| A parameter whose type is a type parameter, marked `[NonCopyable]` or not | No |
| A parameter whose declared type mentions a type parameter that is not marked (`ArrayPool<T>.Return(T[])`), unless the list marks the member `borrows` (`Array.Reverse<T>(T[])`) | No |
| A parameter of another type than the local's own, an implicit `params` collection, a conversion, an operator, a handler | No |
| A parameter of the local's own type, of a method, a local function or a delegate that hands back no reference and no view | Yes |

The last row is for a local that holds values directly: see [Returning a container](returning-containers.md) for the whole list.

A method that keeps a container parameter without the mark is reported where it first does so (`SCA1032`): it assigns it to a field, an
element or a property, writes it through a reference, captures it in a lambda or in a delegate for one of its methods, or passes it on to
something that keeps it. A parameter whose type is a type parameter is such a thing, marked `[NonCopyable]` or not, by value or by
reference: `static void Register(List<LockGuard> list) => s_registered.Add(list);` keeps the list in a collection of the project's own
just as it would in a <xref:System.Collections.Generic.List`1>. A pure move is not: what goes into `NonCopyable.Move(ref list)` comes out as its result.

**A parameter that is not `[Kept]` is held to a list of what is allowed,** not to a list of the ways of keeping it: a list of those
always misses one. It is a list of its own, and shorter questions than those for a [local that the method owns](returning-containers.md):
keeping only asks whether something still holds the container when the method has returned. Both lists read one description of each
use: what the use makes of the container, which is nothing, a reference, a view that is a ref struct, or the container under another
name, and whether what receives the container keeps. They differ in where that may go. A parameter is followed through the variables
of the method that hold it, and a local allows only a use on the spot.

- **Allowed:** any member of its own that is used on it, also with `?.`, whatever the member takes in or hands out; a field or a
  property of it that is read or set, and an element that is used in place; a reference or a view of it that stays in the method
  (`ref LockGuard first = ref span[0];`, `Span<LockGuard> view = CollectionsMarshal.AsSpan(list);`); passing it, by value or by
  reference and whatever the declared type of the parameter, to a parameter that does not keep
  (`ArgumentNullException.ThrowIfNull(list);`); a test, a pattern, a `foreach`, an `await`, a spread and a `with`, as far as the
  members that those call are its own; returning it, which is judged [by itself](returning-containers.md); and a write of the
  variable.
- **Keeping:** a store into anything but a local of the method, which is the container under another name and is followed; a capture;
  and passing it in any form to what keeps, as listed below.
- **Everything else that takes it as a value is reported too** (`SCA1032`, with a text of its own): a form that nobody named is a
  finding and not a gap. A member that is called on a `dynamic` reference to it and `__makeref` are such forms.

What this does not see is a member of the framework that keeps an argument which it takes as `object`, such as the state of
`CancellationToken.UnsafeRegister(callback, state)`: nothing in its signature says so. That is the business of the
[conversion that hides the type](conversions.md) and of [the safe list](../guides/symbol-list.md).

## Methods That Go On After They Return

**A method that goes on after it has returned keeps what it still uses.** An iterator runs its whole body after the call that creates it
has returned, and an `async` method everything that follows its first `await`. So an iterator keeps every container parameter that it uses,
and an `async` method every one that it uses on a path after an `await`, `await foreach` and `await using` included (`SCA1032`). A lambda
and a local function are judged the same way. What an `async` method does before its first `await` runs during the call and keeps nothing.

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

The mark is in the signature, so it holds through an interface, an override and a referenced assembly, where the caller cannot see how
the method is written: an interface method whose `async` implementation needs the mark needs it too.

## Examples

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

## The Kept Mark, Method Groups and Inherited Members

`[Kept]` is kept like the other marks: an override or an implementation must not have it where the member that it replaces does not, and a
method or a lambda with the mark cannot be converted to a delegate whose parameter is not taken to keep what it is given (`SCA1040`). For
both, a parameter is taken to keep what it is given if it is marked `[Kept]`, if it is the value of a setter, or if its type is a type
parameter that is not marked `[NonCopyable]`. So `Action<List<LockGuard>> keep = Register;` is fine: what is passed to the type parameter
of `Action<T>` counts as kept. A `[MovedFrom]` parameter hands its container over for good, so the method may keep it.

**A method group keeps what its method keeps.** A parameter whose type is a type parameter is taken to keep what it is given, marked or
not, so `registry.Add` of a `Registry<List<LockGuard>>` keeps the list that it is called with. Converted to a delegate whose parameter is
a container and is not taken to keep, it is reported like an added `[Kept]` (`SCA1040`): the callers of the delegate take what they pass
to be theirs still. A method group is asked what every call is asked. So `Recycler recycle = ArrayPool<LockGuard>.Shared.Return;` is
reported for a `delegate void Recycler(LockGuard[] buffer, bool clear);`, whose parameter does not keep, and a method group of
<xref:System.Array.Reverse*?displayProperty=nameWithType>, which the list marks `borrows`, is not. `Action<LockGuard[], bool> recycle = ArrayPool<LockGuard>.Shared.Return;` is
not reported either: the parameters of `Action<T1, T2>` are type parameters, which are taken to keep as well.
The same goes for a delegate that is made from another delegate. The parameters are lined up first: a method
group of an extension method that is called on a receiver, as `holder.Keep` is, has one parameter more than the delegate, and its
first one is the receiver. `[MovedFrom]` is compared for the same parameters.

**An inherited member that implements an interface member is compared in the same way.** `class GuardsRegistry :
Registry<List<LockGuard>>, IGuardsSink` implements `IGuardsSink.Add(List<LockGuard>)` with `Registry<T>.Add(T)`, whose parameter is
a type parameter and is taken to keep. A caller through the interface takes its list to be its own still, so the class is reported
(`SCA1040`). Implement the member in the class, or mark the parameter of the interface member `[Kept]`. An inherited member whose
parameter mentions a type parameter that is not marked, as `Pool<T>.Return(T[])` does, is reported in the same way unless its list
entry is marked `borrows`.

```cs
public delegate void GuardsSink(List<LockGuard> guards);
public delegate void KeptSink([Kept] List<LockGuard> guards);

GuardsSink onCreated = registry.Add;        // SCA1040: 'Registry<T>.Add(T)' keeps what it is given
KeptSink keeping = registry.Add;            // OK: the delegate says that it keeps
Action<List<LockGuard>> action = registry.Add;   // OK: the parameter of 'Action<T>' is a type parameter as well
```

## What the Check Follows

The check of a method body follows the parameter wherever the code of the method takes it:

- **Into every variable that holds it:** a local or a by-value parameter that it is assigned to, declared with or matched into by a
  pattern, in whatever order the statements stand. A local that is stored at the top of a loop and assigned the parameter further down
  holds the parameter all the same.
- **Under another name:** what is made of the container can be the container in another form. That is a view that is no ref struct, as a
  <xref:System.Memory`1> and an <xref:System.ArraySegment`1> are (`_pending = guards;`, `_pending = guards.AsMemory(0, 1);`), the container as another type
  that a listed member hands out (`segment.Array`, `list.AsReadOnly()`, `task.WaitAsync(timeout)`, also through an `out` parameter, as
  with `MemoryMarshal.TryGetArray(memory, out var segment)`), and the variable itself where a callee is given it by reference and
  returns a reference (`_all = Pick(ref list);`). Each is followed like the container: stored in a field, in an object initializer or
  in a field of a static struct, it is the container that is kept. A span or a reference that stays in the method keeps nothing. What a
  member that is listed as `new` hands out, such as <xref:System.Buffers.ArrayPool`1.Rent*?displayProperty=nameWithType>, is taken out of the container for good and is no other name
  for it. Code of the project hands back a value of its own as far as its return is checked, and two returns are not: that of a
  <xref:System.Memory`1> or a <xref:System.ReadOnlyMemory`1>, and that of a getter or an indexer. So a memory that a method, a local function or a
  delegate hands back when it is given the container is the container under another name: with
  `static Memory<LockGuard> Window(LockGuard[] buffer, int count)`, `_pending = Window(buffer, 1);` is a store of `buffer`. A task
  of a memory from an `async` method is judged like the memory, and so is what an `await` of it gives. And whatever a getter or an
  indexer hands back that holds a non-copyable type is the container that it was given, as an argument or as the receiver of an
  extension block: `public ArraySegment<LockGuard> Segment => buffer;` in `extension(LockGuard[] buffer)`, and
  `public IReadOnlyList<LockGuard> this[List<LockGuard> key] => key;`. A property of the container's own type is not such a getter:
  `waiter.Next` hands out what the object holds.
- **Through `with`:** `with` gives a copy of what it is applied to, which holds what that holds. `_pending = window with { };` on a
  `Memory<LockGuard>` over the parameter, and `_entry = entry with { Count = 1 };` on a tuple that holds it, are stores of the
  container.
- **Into a collection that is made:** an element of an array or of a collection expression is kept by it. So is each argument for a
  `params` parameter whose type is not an array or a span, since the builder or the `Add` of that type receives it, and each element
  of a parameter that holds containers where a spread hands them on: `_all = [.. lists];` keeps what `lists` holds, as a `foreach`
  that adds them would.
- **Through a member that the compiler calls:** some kinds of operation run code with the container as its receiver or argument where
  nothing but what the compiler bound shows it. Each such member is judged like the same member written out, so one that is an
  extension with a `[Kept]` receiver keeps the container: the call behind `?.` (`guards?.Register();`); `Deconstruct` of a
  deconstruction and of a positional pattern; `GetAwaiter` of an `await`; `GetEnumerator` and `GetAsyncEnumerator` of a `foreach`
  and of a spread; a property of a property pattern, and the length, the indexer and `Slice` of a list pattern;
  `operator true` and `operator false` of a user-defined `&&` and `||`; the constructor of an interpolated-string handler
  that a call passes its argument on to through `[InterpolatedStringHandlerArgument]`; the operator of `guards += 1` and `guards++`
  that is an instance member of an extension block; the conversion that a `??` applies to its left side and that a deconstruction or
  a `foreach` applies to an element (`(Handle handle, int count) = pair;`); the `Slice` and the `Length` or `Count` behind a range or
  an index from the end (`guards[1..]`); and the method of a method group (`Action register = guards.Register;`). An operation
  that is allowed is allowed with every member that it carries: the analyzer keeps a list of the kinds of operation that a
  container may be used in, with every member that each kind carries, and a test holds that list against the compiler's API, so
  that a member which a later compiler adds is found there. A statement with no operation of its own, as `fixed` is, is not on the
  list, and a container that it uses is reported as used in a way that is not followed.
- **Through an indexer:** the parameter of an indexer keeps where the indexer is assigned, like the parameter of a setter:
  `map[guards] = 1;`, `map[guards] += 1;` and `new Map { [guards] = 1 }`. Where the indexer is read, its getter is a member like any
  other, and what it hands back is followed.
- **Through a reference:** a write through a reference keeps the container whatever the reference refers to. That is a `ref` local, a `ref`
  or `out` parameter of the method, what a call returns by reference, a field or an element that is reached through one of those, and what
  a pointer points to. Making a `ref` local refer to the parameter keeps nothing.
- **Inside a tuple:** a tuple carries the container to wherever the tuple goes. `_pair = (list, 1)` and `(_all, _count) = (list, 1)` keep
  it. A tuple that is taken apart, by a deconstruction, a pattern or a `switch`, gives it to the variables that take its elements, and
  those are followed in turn. Where nothing says which variable takes which element, as with a tuple in a variable, each variable that
  could take it counts.
- **Inside a delegate:** `_onClose = list.Clear;` keeps the list as `_onClose = () => list.Clear();` does. A delegate in a local of the
  method is followed like a tuple, and one that is returned or passed on keeps the list.
- **Through an operator:** a user-defined conversion or operator is a call of its method, and keeps what it is given if the parameter of
  the method is marked `[Kept]`. That includes `++` and `--`. So does `AppendFormatted` of an interpolated-string handler whose
  parameter is marked, or whose parameter is a type parameter that is marked `[NonCopyable]`: such a handler says that it moves what a
  hole gives it. The handlers of the framework format it.
- **To a type parameter, in every form:** a parameter whose type is a type parameter is taken to keep what it is given, marked or not.
  That holds for an argument (`bag.Add(list)`), for an element of the collection that the compiler makes for `params T[]` or
  `params ReadOnlySpan<T>` (`bag.AddRange(list)`), for the operand of an operator or a conversion of a generic (`bag += list`,
  `_ = bag + list`, `Handle<List<LockGuard>> handle = list;`), and for the receiver of an extension block over a type parameter
  (`extension<[NonCopyable] T>(T item)`). A [pure move](../guides/moving-a-value.md) is the exception.
- **To code that nothing checks:** a parameter whose declared type mentions a type parameter that is not marked is taken to keep what it
  is given, as an argument, an operand or the receiver of an extension block. `ArrayPool<LockGuard>.Shared.Return(buffer)` keeps the
  array and `Array.Reverse(guards)` only uses it, and no declaration tells the two apart. So such a parameter keeps unless
  [the safe list](../guides/symbol-list.md) marks the member `borrows`, which says that the member uses what it is given during the call and
  keeps nothing of it. A constructor of a ref struct that the list has, such as `new Span<LockGuard>(buffer)`, is a call that returns
  a ref struct, and keeps nothing more than `buffer.AsSpan()` does. The built-in list has the marker on every member of this shape that the runtime's source shows to keep
  nothing, <xref:System.Array.Reverse*?displayProperty=nameWithType> and the comparisons of <xref:System.Memory`1> and <xref:System.ArraySegment`1> among them, and a member of your own list gets it
  the same way. A call that hands the container back in another form needs no marker, whether as a reference, a ref struct, another
  type or through an `out` parameter: that is what the call is for, and what it hands back is followed
  (`CollectionsMarshal.AsSpan(list)`, `guards.AsMemory()`). The finding has a text of its own, which names the entry to add with
  `borrows` if the member keeps nothing and `[Kept]` on the parameter if it does. A use of a member that is not listed at all is
  reported as that (`SCA1020`) and not again as keeping. For a generic helper of the project's own, mark its type parameter
  `[NonCopyable]` instead of listing the helper: its body is then checked, and its parameter says itself whether it keeps.
- **Out of a parameter that holds containers:** an element of it, by an index or a `foreach`, is a container that the caller put in.
  `static void Register(params List<LockGuard>[] lists) => _first = lists[0];` keeps it. So is what a listed member of a struct
  hands out of it, as `entry.Value` of a `KeyValuePair<string, List<LockGuard>>` does.
- **As the receiver of an extension block:** `extension(List<LockGuard> list) { public void Register() => _all = list; }` keeps its
  receiver, and is reported unless the receiver is marked.
- **Through a call that cannot be seen:** a dynamic call, a call through a function pointer and `__arglist` keep what they are given.

## Wrapping a Task

**A method that wraps a task which it was given cannot be written clean.** `ValueTask<LockGuard> Wrap(Task<LockGuard> task) => new(task);`
keeps the task (`SCA1032`) and reads from a stored one (`SCA1030`), and no annotation changes the second: a caller could pass the same
field twice. `[Kept]` only moves the question to the caller. Return the `Task<LockGuard>` as it is, take the task through a
`[MovedFrom] ref` parameter, which the caller has to own, or suppress the finding in the wrapper.

</div>
